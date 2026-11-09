using FluentValidation;

namespace AqtCoreFW.Rules.Application.Commands.RunTestCase;

public sealed class RunTestCaseCommandValidator : AbstractValidator<RunTestCaseCommand>
{
    public RunTestCaseCommandValidator()
    {
        RuleFor(x => x.TestCaseId).GreaterThan(0L);
    }
}
