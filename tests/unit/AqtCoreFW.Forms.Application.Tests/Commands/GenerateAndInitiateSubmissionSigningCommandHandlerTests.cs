using AqtCoreFW.Forms.Application.Commands.SignSubmissionDocument;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for GenerateAndInitiateSubmissionSigningCommandHandler.
/// Covers submission lookup with cross-tenant isolation, PDF composition, bucket upload,
/// orchestrator error mapping, at-most-once linking, and FE handoff.
/// </summary>
public sealed class GenerateAndInitiateSubmissionSigningCommandHandlerTests
{
    private readonly IFormSubmissionRepository _submissionRepo;
    private readonly ISubmissionPdfComposer _pdfComposer;
    private readonly ISigningOriginalBucketUploader _bucketUploader;
    private readonly ISigningRequestClient _signingClient;
    private readonly ILogger<GenerateAndInitiateSubmissionSigningCommandHandler> _logger;
    private readonly IGuidGenerator _guidGen;
    private readonly GenerateAndInitiateSubmissionSigningCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestSubmissionId = Guid.NewGuid();

    public GenerateAndInitiateSubmissionSigningCommandHandlerTests()
    {
        _submissionRepo = Substitute.For<IFormSubmissionRepository>();
        _pdfComposer = Substitute.For<ISubmissionPdfComposer>();
        _bucketUploader = Substitute.For<ISigningOriginalBucketUploader>();
        _signingClient = Substitute.For<ISigningRequestClient>();
        _logger = Substitute.For<ILogger<GenerateAndInitiateSubmissionSigningCommandHandler>>();
        _guidGen = Substitute.For<IGuidGenerator>();

        // Default: guid gen returns new guid on each call
        _guidGen.NewIdRandom().Returns(_ => Guid.NewGuid());
        _guidGen.NewIdMonotonic().Returns(_ => Guid.CreateVersion7());

        _handler = new GenerateAndInitiateSubmissionSigningCommandHandler(
            _submissionRepo,
            _pdfComposer,
            _bucketUploader,
            _signingClient,
            _logger,
            _guidGen);
    }

    // ─── Submission lookup & tenant isolation ──────────────────────────────────────

    [Fact]
    public async Task Handle_SubmissionNotFound_ReturnsNotFoundError()
    {
        _submissionRepo
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<FormSubmission>("not found")));

        var command = BuildCommand();
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CrossTenantSubmission_ReturnsNotFoundError()
    {
        var otherTenantId = Guid.NewGuid();
        var submission = CreateSubmission(otherTenantId);

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().HaveCount(1);
    }

    // ─── At-most-once linking guard ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_SubmissionAlreadyLinked_ReturnsValidationError()
    {
        var submission = CreateSubmission(TestTenantId);
        submission.LinkSigningRequest(Guid.NewGuid()); // Already linked

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().AllSatisfy(e =>
            e.Message.Should().Contain("GOV_FRM_030"));
    }

    // ─── PDF composition ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PdfCompositionFails_ReturnsFailed()
    {
        var submission = CreateSubmission(TestTenantId);
        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<ComposedSubmissionPdf>("PDF composition failed")));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // ─── Bucket upload ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_BucketUploadThrows_ReturnsErrorAndLogsWarning()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var uploadException = new InvalidOperationException("S3 connection failed");
        _bucketUploader
            .UploadAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromException<string>(uploadException));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("thất bại");
    }

    [Fact]
    public async Task Handle_BucketUpload_UsesTenantPrefixedKey()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var successOutcome = new SigningRequestOutcome
        {
            IsSuccess = true,
            RequestPublicId = Guid.NewGuid(),
            ClientHandoff = new Dictionary<string, string> { ["handlerUrl"] = "https://example.com" }
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successOutcome));

        _submissionRepo
            .UpdateAsync(Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        await _handler.Handle(command, CancellationToken.None);

        // Verify bucket upload was called with tenant-prefixed key
        await _bucketUploader.Received(1).UploadAsync(
            Arg.Is<byte[]>(b => b.SequenceEqual(pdfBytes)),
            Arg.Is<string>(key => key.StartsWith($"{TestTenantId}/")),
            Arg.Any<CancellationToken>());
    }

    // ─── Orchestrator interaction & error mapping ─────────────────────────────────

    [Fact]
    public async Task Handle_OrchestratorErrorSIGNING_VALIDATION_FAILED_ReturnsValidationError()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var failureOutcome = new SigningRequestOutcome
        {
            IsSuccess = false,
            ErrorCode = "SIGNING_VALIDATION_FAILED",
            ErrorMessage = "Provider validation failed"
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failureOutcome));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().AllSatisfy(e =>
            e.Should().BeAssignableTo<ValidationError>());
    }

    [Fact]
    public async Task Handle_OrchestratorErrorSIGNING_ROUTE_UNSUPPORTED_ReturnsBadRequestError()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var failureOutcome = new SigningRequestOutcome
        {
            IsSuccess = false,
            ErrorCode = "SIGNING_ROUTE_UNSUPPORTED",
            ErrorMessage = "No route available for this provider"
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failureOutcome));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().AllSatisfy(e =>
            e.Should().BeAssignableTo<BadRequestError>());
    }

    [Fact]
    public async Task Handle_OrchestratorErrorSIGNING_NOT_FOUND_ReturnsNotFoundError()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var failureOutcome = new SigningRequestOutcome
        {
            IsSuccess = false,
            ErrorCode = "SIGNING_NOT_FOUND",
            ErrorMessage = "Provider not found"
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failureOutcome));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().AllSatisfy(e =>
            e.Should().BeAssignableTo<NotFoundError>());
    }

    [Fact]
    public async Task Handle_OrchestratorErrorSIGNING_FAILED_ReturnsGenericError()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var failureOutcome = new SigningRequestOutcome
        {
            IsSuccess = false,
            ErrorCode = "SIGNING_FAILED",
            ErrorMessage = "Unknown signing failure"
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failureOutcome));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        // Generic Error (not ValidationError, BadRequestError, or NotFoundError) → 500
        result.Errors.Should().AllSatisfy(e =>
        {
            e.Should().NotBeAssignableTo<ValidationError>();
            e.Should().NotBeAssignableTo<BadRequestError>();
            e.Should().NotBeAssignableTo<NotFoundError>();
        });
    }

    [Fact]
    public async Task Handle_OrchestratorErrorSIGNING_SEQUENCE_X_ReturnsBadRequestError()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var failureOutcome = new SigningRequestOutcome
        {
            IsSuccess = false,
            ErrorCode = "SIGNING_SEQUENCE_MISMATCH",
            ErrorMessage = "Sequence number mismatch"
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failureOutcome));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().AllSatisfy(e =>
            e.Should().BeAssignableTo<BadRequestError>());
    }

    [Fact]
    public async Task Handle_OrchestratorFailure_LogsWarningWithOrphanKey()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var failureOutcome = new SigningRequestOutcome
        {
            IsSuccess = false,
            ErrorCode = "SIGNING_FAILED",
            ErrorMessage = "Signing failed"
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failureOutcome));

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        await _handler.Handle(command, CancellationToken.None);

        // Verify logger was called with warning about orphan key
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Orphaned")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // ─── Success path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SuccessfulRequest_ReturnsResultWithRequestIdAndClientHandoff()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var requestId = Guid.NewGuid();
        var clientHandoff = new Dictionary<string, string>
        {
            ["handlerUrl"] = "https://api.example.com/signing/docflow/file",
            ["jwt"] = "eyJhbGc...",
            ["docId"] = Guid.NewGuid().ToString("D")
        };

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var successOutcome = new SigningRequestOutcome
        {
            IsSuccess = true,
            RequestPublicId = requestId,
            ClientHandoff = clientHandoff
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successOutcome));

        _submissionRepo
            .UpdateAsync(Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequestId.Should().Be(requestId);
        result.Value.ClientHandoff.Should().BeEquivalentTo(clientHandoff);
    }

    [Fact]
    public async Task Handle_SuccessfulRequest_LinksSigningRequestToSubmission()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var requestId = Guid.NewGuid();

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var successOutcome = new SigningRequestOutcome
        {
            IsSuccess = true,
            RequestPublicId = requestId,
            ClientHandoff = new Dictionary<string, string> { ["handlerUrl"] = "https://example.com" }
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successOutcome));

        _submissionRepo
            .UpdateAsync(Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        await _handler.Handle(command, CancellationToken.None);

        // Verify submission was updated with the signing request id
        await _submissionRepo.Received(1).UpdateAsync(
            Arg.Is<FormSubmission>(s => s.SigningRequestId == requestId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SuccessfulRequest_PassesCorrectParametersToClient()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var signType = "Issued";
        var docNumber = "QĐ/2026/001";
        var issuedDate = new DateTime(2026, 6, 17);
        var providerKey = "usb-vgca";
        var baseUrl = "https://api.example.com";

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var successOutcome = new SigningRequestOutcome
        {
            IsSuccess = true,
            RequestPublicId = Guid.NewGuid(),
            ClientHandoff = new Dictionary<string, string> { ["handlerUrl"] = "https://example.com" }
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successOutcome));

        _submissionRepo
            .UpdateAsync(Arg.Any<FormSubmission>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var command = new GenerateAndInitiateSubmissionSigningCommand(
            submission.PublicId, TestTenantId, TestUserId, signType, docNumber, issuedDate, providerKey, baseUrl);

        await _handler.Handle(command, CancellationToken.None);

        // Verify client was called with correct parameters
        await _signingClient.Received(1).RequestSignatureAsync(
            Arg.Is<SigningRequestClientInput>(input =>
                input.TenantId == TestTenantId &&
                input.ArtifactKind == "FormPdf" &&
                input.ProviderKey == providerKey &&
                input.AssignedSignerId == TestUserId &&
                input.SignerSequenceNumber == 1 &&
                input.DocFlow != null &&
                input.DocFlow.SignType == signType &&
                input.DocFlow.DocNumber == docNumber &&
                input.DocFlow.IssuedDate == issuedDate &&
                input.DocFlow.BaseUrl == baseUrl),
            Arg.Any<CancellationToken>());
    }

    // ─── LinkSigningRequest race condition ─────────────────────────────────────────

    [Fact]
    public async Task Handle_LinkSigningRequestRace_ReturnsConcurrencyError()
    {
        var submission = CreateSubmission(TestTenantId);
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var requestId = Guid.NewGuid();

        _submissionRepo
            .GetByIdAsync(submission.PublicId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(submission)));

        _pdfComposer
            .ComposeAsync(submission.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new ComposedSubmissionPdf(pdfBytes, submission.PublicId))));

        var successOutcome = new SigningRequestOutcome
        {
            IsSuccess = true,
            RequestPublicId = requestId,
            ClientHandoff = new Dictionary<string, string> { ["handlerUrl"] = "https://example.com" }
        };

        _signingClient
            .RequestSignatureAsync(Arg.Any<SigningRequestClientInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successOutcome));

        // Simulate race: submission already has a signing request linked from another concurrent request.
        // When the handler calls LinkSigningRequest, it will throw InvalidOperationException.
        // This is caught and converted to ValidationError by the handler (line 162-165 in handler).
        submission.LinkSigningRequest(Guid.NewGuid()); // Pre-link to simulate race

        var command = BuildCommand(submissionPublicId: submission.PublicId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        // The handler catches InvalidOperationException from LinkSigningRequest and returns ValidationError
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().BeAssignableTo<ValidationError>();
        result.Errors[0].Message.Should().Contain("GOV_FRM_030");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────

    private FormSubmission CreateSubmission(Guid tenantId)
    {
        // Use Create factory method with a temporary guid generator
        var submission = FormSubmission.Create(
            _guidGen,
            tenantId,
            formTemplateId: 1L,
            formTemplateVersion: 1,
            submissionData: "{}",
            submittedBy: TestUserId,
            consentGiven: false);

        // Manually set PublicId to our test value via reflection (since Create generates a new random one)
        var pubIdProp = typeof(FormSubmission).GetProperty("PublicId")!;
        pubIdProp.SetValue(submission, TestSubmissionId);

        return submission;
    }

    private GenerateAndInitiateSubmissionSigningCommand BuildCommand(
        Guid? submissionPublicId = null,
        Guid? tenantId = null,
        Guid? initiatedBy = null,
        string? signType = null,
        string? docNumber = null,
        DateTime? issuedDate = null,
        string? providerKey = null,
        string? baseUrl = null) =>
        new(
            submissionPublicId ?? TestSubmissionId,
            tenantId ?? TestTenantId,
            initiatedBy ?? TestUserId,
            signType ?? "Issued",
            docNumber ?? "QĐ/2026/001",
            issuedDate ?? new DateTime(2026, 6, 17),
            providerKey ?? "usb-vgca",
            baseUrl ?? "https://api.example.com");
}
