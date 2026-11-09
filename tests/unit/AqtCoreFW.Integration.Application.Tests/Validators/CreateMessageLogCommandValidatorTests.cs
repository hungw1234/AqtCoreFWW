using AqtCoreFW.Integration.Application.Commands.CreateMessageLog;
using AqtCoreFW.Integration.Domain.Enums;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Integration.Application.Tests.Validators;

/// <summary>
/// Unit tests for CreateMessageLogCommandValidator.
/// Phase-07 PR-B: PartnerPublicId (Guid, T2 AK), ContractPublicId (Guid?, T2 AK optional).
/// </summary>
public sealed class CreateMessageLogCommandValidatorTests
{
    private readonly CreateMessageLogCommandValidator _sut = new();

    private static readonly Guid SamplePartnerPublicId = Guid.NewGuid();

    private static CreateMessageLogCommand MakeCmd(
        Guid? tenantId = null,
        Guid? partnerPublicId = null,
        Guid? contractPublicId = null,         // null = no contract (valid)
        MessageDirection direction = MessageDirection.Outbound,
        string messageType = "GOV_NOTIFY",
        string? payload = null,
        string? correlationId = "corr-001") =>
        new(tenantId ?? Guid.NewGuid(),
            partnerPublicId ?? SamplePartnerPublicId,
            contractPublicId,
            direction, messageType, payload, correlationId);

    [Fact]
    public void Valid_Command_Passes()
    {
        var result = _sut.TestValidate(MakeCmd());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_TenantId_Fails() =>
        _sut.TestValidate(MakeCmd(tenantId: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.TenantId);

    [Fact]
    public void Empty_PartnerPublicId_Fails() =>
        _sut.TestValidate(MakeCmd(partnerPublicId: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.PartnerPublicId);

    [Fact]
    public void Invalid_Direction_Fails() =>
        _sut.TestValidate(MakeCmd(direction: (MessageDirection)99))
            .ShouldHaveValidationErrorFor(x => x.Direction);

    [Fact]
    public void Inbound_Direction_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(direction: MessageDirection.Inbound));
        result.ShouldNotHaveValidationErrorFor(x => x.Direction);
    }

    [Fact]
    public void Empty_MessageType_Fails() =>
        _sut.TestValidate(MakeCmd(messageType: ""))
            .ShouldHaveValidationErrorFor(x => x.MessageType);

    [Fact]
    public void MessageType_Exceeds_200_Fails() =>
        _sut.TestValidate(MakeCmd(messageType: new string('x', 201)))
            .ShouldHaveValidationErrorFor(x => x.MessageType);

    [Fact]
    public void CorrelationId_Exceeds_200_Fails() =>
        _sut.TestValidate(MakeCmd(correlationId: new string('x', 201)))
            .ShouldHaveValidationErrorFor(x => x.CorrelationId);

    [Fact]
    public void Null_CorrelationId_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(correlationId: null));
        result.ShouldNotHaveValidationErrorFor(x => x.CorrelationId);
    }

    [Fact]
    public void Null_ContractPublicId_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(contractPublicId: null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_ContractPublicId_Fails()
    {
        // When ContractPublicId is provided (HasValue = true), Guid.Empty is rejected.
        // Use a nullable Guid set to Guid.Empty to trigger the When() guard.
        Guid? emptyGuid = Guid.Empty;
        _sut.TestValidate(MakeCmd(contractPublicId: emptyGuid))
            .ShouldHaveValidationErrorFor(x => x.ContractPublicId);
    }

    [Fact]
    public void Valid_ContractPublicId_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(contractPublicId: Guid.NewGuid()));
        result.ShouldNotHaveValidationErrorFor(x => x.ContractPublicId);
    }
}
