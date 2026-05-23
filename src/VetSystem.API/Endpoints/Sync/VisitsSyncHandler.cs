using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Visits;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/visits</c> (M5 task 18) — the offline write path for visits, enforcing the field-visit
/// conflict rule (PRD §8.4): the doctor-device is authoritative for medical content while the
/// server's copy is <c>open</c>/<c>in_progress</c>, but once it is <c>completed</c>/<c>cancelled</c>
/// the server wins and a conflicting client edit is rejected (the client re-pulls the authoritative
/// copy). PUT replays the device's record as-is (it may already be closed). Status changes go
/// through the same state machine as the online endpoints.
/// </summary>
public sealed class VisitsSyncHandler : ISyncTableHandler
{
    public const string TableName = "visits";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;
    private readonly IClock _clock;
    private readonly IVisitNumberValidator _visitNumbers;

    public VisitsSyncHandler(
        ApplicationDbContext db,
        ICurrentUserAccessor user,
        IClock clock,
        IVisitNumberValidator visitNumbers)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _visitNumbers = visitNumbers;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var existing = await _db.Visits.IgnoreQueryFilters().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException("visit_already_exists", $"Visit '{id}' already exists; use PATCH to update.");
        }

        var customerId = SyncBody.RequireGuid(body, "customer_id");
        await EnsureExistsAsync(_db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken), "customer", customerId);

        var doctorId = SyncBody.RequireGuid(body, "doctor_id");
        await EnsureExistsAsync(_db.Users.AnyAsync(u => u.Id == doctorId, cancellationToken), "doctor", doctorId);

        var petId = SyncBody.OptionalGuid(body, "pet_id");
        if (petId is { } pid)
        {
            var pet = await _db.Pets.FirstOrDefaultAsync(p => p.Id == pid, cancellationToken)
                      ?? throw new NotFoundException("pet", pid);
            if (pet.CustomerId != customerId)
            {
                throw new ConflictException("pet_customer_mismatch", "The pet does not belong to the visit's customer.");
            }
        }

        var visitNumber = SyncBody.OptionalString(body, "visit_number");
        if (!string.IsNullOrWhiteSpace(visitNumber))
        {
            await _visitNumbers.ValidateAsync(visitNumber, excludeVisitId: null, cancellationToken);
        }
        else
        {
            visitNumber = null;
        }

        var status = SyncBody.OptionalString(body, "status") ?? VisitStatus.Open;
        if (!VisitStatus.All.Contains(status))
        {
            throw new ConflictException("invalid_visit_status", $"status '{status}' is not valid.");
        }

        var startedAt = SyncBody.OptionalDateTime(body, "started_at") ?? _clock.UtcNow;
        var endedAt = SyncBody.OptionalDateTime(body, "ended_at");
        if (VisitStatus.IsTerminal(status) && endedAt is null)
        {
            endedAt = _clock.UtcNow;
        }

        var visit = new Visit
        {
            Id = id,
            VisitType = SyncBody.RequireString(body, "visit_type", VisitType.All, TableName),
            VisitNumber = visitNumber,
            CustomerId = customerId,
            PetId = petId,
            DoctorId = doctorId,
            ReceptionistId = SyncBody.OptionalGuid(body, "receptionist_id"),
            Status = status,
            StartedAt = startedAt,
            EndedAt = endedAt,
            ChiefComplaint = SyncBody.OptionalString(body, "chief_complaint"),
            Symptoms = SyncBody.OptionalString(body, "symptoms"),
            Temperature = SyncBody.OptionalDecimal(body, "temperature"),
            HeartRate = SyncBody.OptionalInt(body, "heart_rate"),
            RespiratoryRate = SyncBody.OptionalInt(body, "respiratory_rate"),
            Weight = SyncBody.OptionalDecimal(body, "weight"),
            ClinicalNotes = SyncBody.OptionalString(body, "clinical_notes"),
            PreliminaryDiagnosis = SyncBody.OptionalString(body, "preliminary_diagnosis"),
            FinalDiagnosis = SyncBody.OptionalString(body, "final_diagnosis"),
            Severity = ValidateSeverity(SyncBody.OptionalString(body, "severity")),
            IcdVetCode = SyncBody.OptionalString(body, "icd_vet_code"),
            ExamFeeApplied = SyncBody.OptionalDecimal(body, "exam_fee_applied"),
        };

        _db.Visits.Add(visit);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(visit.Id, visit.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                    ?? throw new NotFoundException(TableName, id);

        // Server-wins: a closed visit is authoritative; the offline edit is rejected.
        if (VisitStatus.IsTerminal(visit.Status))
        {
            throw new ConflictException("visit_server_authoritative",
                $"Visit '{id}' is {visit.Status} on the server and cannot be changed; re-pull the authoritative copy.");
        }

        if (SyncBody.TryGetString(body, "status", out var statusValue) && statusValue is not null
            && statusValue != visit.Status)
        {
            if (!VisitStatus.CanTransition(visit.Status, statusValue))
            {
                throw new ConflictException("invalid_status_transition",
                    $"Cannot transition a visit from '{visit.Status}' to '{statusValue}'.");
            }

            visit.Status = statusValue;
            if (VisitStatus.IsTerminal(statusValue))
            {
                visit.EndedAt = SyncBody.OptionalDateTime(body, "ended_at") ?? _clock.UtcNow;
            }
            else if (statusValue == VisitStatus.InProgress)
            {
                visit.StartedAt ??= _clock.UtcNow;
            }
        }

        ApplyMedicalFields(visit, body);

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(visit.Id, visit.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                    ?? throw new NotFoundException(TableName, id);

        if (VisitStatus.IsTerminal(visit.Status))
        {
            throw new ConflictException("visit_server_authoritative",
                $"Visit '{id}' is {visit.Status} on the server and cannot be deleted.");
        }

        _db.Visits.Remove(visit);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(visit.Id, visit.UpdatedAt);
    }

    private void ApplyMedicalFields(Visit visit, JsonElement body)
    {
        if (SyncBody.TryGetString(body, "chief_complaint", out var cc)) visit.ChiefComplaint = cc;
        if (SyncBody.TryGetString(body, "symptoms", out var sym)) visit.Symptoms = sym;
        if (body.TryGetProperty("temperature", out _)) visit.Temperature = SyncBody.OptionalDecimal(body, "temperature");
        if (body.TryGetProperty("heart_rate", out _)) visit.HeartRate = SyncBody.OptionalInt(body, "heart_rate");
        if (body.TryGetProperty("respiratory_rate", out _)) visit.RespiratoryRate = SyncBody.OptionalInt(body, "respiratory_rate");
        if (body.TryGetProperty("weight", out _)) visit.Weight = SyncBody.OptionalDecimal(body, "weight");
        if (SyncBody.TryGetString(body, "clinical_notes", out var cn)) visit.ClinicalNotes = cn;
        if (SyncBody.TryGetString(body, "preliminary_diagnosis", out var pd)) visit.PreliminaryDiagnosis = pd;
        if (SyncBody.TryGetString(body, "final_diagnosis", out var fd)) visit.FinalDiagnosis = fd;
        if (SyncBody.TryGetString(body, "severity", out var sev)) visit.Severity = ValidateSeverity(sev);
        if (SyncBody.TryGetString(body, "icd_vet_code", out var icd)) visit.IcdVetCode = icd;
        if (body.TryGetProperty("exam_fee_applied", out _)) visit.ExamFeeApplied = SyncBody.OptionalDecimal(body, "exam_fee_applied");
    }

    private static string? ValidateSeverity(string? severity)
    {
        if (severity is not null && !Severity.All.Contains(severity))
        {
            throw new ConflictException("invalid_severity", $"severity '{severity}' is not valid.");
        }

        return severity;
    }

    private void RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static async Task EnsureExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }
}
