using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Rules.Application.Commands.RunTestCase;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record RunTestCaseCommand(long TestCaseId) : ICommand<RuleEvaluationResultDto>;
