using Mapster;
using VetSystem.Application.Employees.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Employees.Mapping;

public sealed class EmployeesMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Balance + LedgerStatus default here and are layered on in the service (from the employee ledger).
        config.NewConfig<Employee, EmployeeResponse>();
        config.NewConfig<EmployeePayment, EmployeePaymentResponse>();
    }
}
