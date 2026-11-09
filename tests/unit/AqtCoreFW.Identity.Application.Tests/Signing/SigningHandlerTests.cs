using System.Security.Cryptography;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace AqtCoreFW.Identity.Application.Tests.Signing;

/// <summary>
/// Unit tests for signing command/query handlers.
/// Tests happy path + key failure cases per behavioural checklist.
/// Audit emission assertions verify structured dataSnapshot fields per QĐ 742 §5.1.
/// </summary>
public sealed class SigningHandlerTests
{
    // ── Shared test data ──────────────────────────────────────────────────────

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestActorId  = Guid.NewGuid();
    private static readonly byte[] TestPayload    = [1, 2, 3, 4];
    private static readonly byte[] TestSignature  = [5, 6, 7, 8];
    private static readonly byte[] TestRowVersion = [0xAA, 0xBB, 0xCC, 0xDD, 0x00, 0x01, 0x02, 0x03];

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    private static TenantSignatureConfig MakeConfig(
        bool isEnabled  = true,
        string providerKey = "remote-viettel-mysign")
    {
        return TenantSignatureConfig.Create(
            tenantId:                  TestTenantId,
            providerKey:               providerKey,
            apiCredentialsRef:         null,
            allowedCaRootThumbprints:  string.Empty,
            ocspMode:                  OcspMode.Soft,
            isEnabled:                 isEnabled,
            signFlowMode:              SignatureFlowMode.Async,
            createdBy:                 TestActorId);
    }

    /// <summary>
    /// Substitutes a UserManager whose FindByIdAsync returns <paramref name="user"/> (null when
    /// omitted). UserManager's ctor only requires a non-null store; the rest may be null for a stub.
    /// </summary>
    private static UserManager<ApplicationUser> MakeUserManager(ApplicationUser? user = null)
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var mgr = Substitute.For<UserManager<ApplicationUser>>(
            store, null, null, null, null, null, null, null, null);
        mgr.FindByIdAsync(Arg.Any<string>()).Returns(user);
        return mgr;
    }

    /// <summary>
    /// Creates a stub ISignerBindingGuard that defaults to Bound("test"). Override as needed.
    /// </summary>
    private static ISignerBindingGuard MakeSignerBindingGuard()
    {
        var guard = Substitute.For<ISignerBindingGuard>();
        guard.VerifyAsync(Arg.Any<CertificateInfo>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(SignerBindingVerdict.Bound("test"));
        return guard;
    }

    // ── GetTenantSignatureConfigQueryHandler ──────────────────────────────────

    [Fact]
    public async Task GetConfig_ConfigExists_ReturnsDto()
    {
        var repo   = Substitute.For<ITenantSignatureConfigRepository>();
        var config = MakeConfig();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var handler = new GetTenantSignatureConfigQueryHandler(repo);
        var result  = await handler.Handle(new GetTenantSignatureConfigQuery(TestTenantId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().Be(TestTenantId);
        result.Value.ProviderKey.Should().Be("remote-viettel-mysign");
        result.Value.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetConfig_NoConfig_ReturnsNotFound()
    {
        var repo = Substitute.For<ITenantSignatureConfigRepository>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns((TenantSignatureConfig?)null);

        var handler = new GetTenantSignatureConfigQueryHandler(repo);
        var result  = await handler.Handle(new GetTenantSignatureConfigQuery(TestTenantId), default);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(TestTenantId.ToString()));
    }

    // ── UpdateTenantSignatureConfigCommandHandler ──────────────────────────────

    [Fact]
    public async Task UpdateConfig_NewConfig_CallsAddAsync()
    {
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns((TenantSignatureConfig?)null);

        var handler = new UpdateTenantSignatureConfigCommandHandler(repo, auditLogger);
        var cmd     = new UpdateTenantSignatureConfigCommand(
            TestTenantId, "remote-viettel-mysign", null, string.Empty,
            OcspMode.Soft, true, SignatureFlowMode.Async, TestActorId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        await repo.Received(1).AddAsync(Arg.Any<TenantSignatureConfig>(), default);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<TenantSignatureConfig>(), Arg.Any<byte[]?>(), default);
    }

    [Fact]
    public async Task UpdateConfig_ExistingConfig_CallsUpdateAsync()
    {
        var existing    = MakeConfig();
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(existing);

        var handler = new UpdateTenantSignatureConfigCommandHandler(repo, auditLogger);
        var cmd     = new UpdateTenantSignatureConfigCommand(
            TestTenantId, "usb-vgca", null, string.Empty,
            OcspMode.Hard, false, SignatureFlowMode.Async, TestActorId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        await repo.DidNotReceive().AddAsync(Arg.Any<TenantSignatureConfig>(), default);
        await repo.Received(1).UpdateAsync(existing, null, default);
    }

    [Fact]
    public async Task UpdateConfig_ExistingConfig_ForwardsRowVersionToRepository()
    {
        var existing    = MakeConfig();
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(existing);

        var handler = new UpdateTenantSignatureConfigCommandHandler(repo, auditLogger);
        var cmd     = new UpdateTenantSignatureConfigCommand(
            TestTenantId, "usb-vgca", null, string.Empty,
            OcspMode.Hard, false, SignatureFlowMode.Async, TestActorId, RowVersion: TestRowVersion);

        await handler.Handle(cmd, default);

        // Repository must receive the supplied RowVersion for optimistic concurrency check.
        await repo.Received(1).UpdateAsync(existing, TestRowVersion, default);
    }

    [Fact]
    public async Task UpdateConfig_EmitsAuditEntry()
    {
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns((TenantSignatureConfig?)null);

        var handler = new UpdateTenantSignatureConfigCommandHandler(repo, auditLogger);
        var cmd     = new UpdateTenantSignatureConfigCommand(
            TestTenantId, "remote-viettel-mysign", null, string.Empty,
            OcspMode.Soft, true, SignatureFlowMode.Async, TestActorId);

        await handler.Handle(cmd, default);

        await auditLogger.Received(1).LogCommandAsync(
            action:       Arg.Any<string>(),
            moduleName:   "Identity",
            resourceType: "TenantSignatureConfig",
            resourceId:   TestTenantId.ToString(),
            commandType:  typeof(UpdateTenantSignatureConfigCommand),
            dataSnapshot: Arg.Is<string?>(s => s != null),
            cancellationToken: default);
    }

    [Fact]
    public async Task UpdateConfig_RootPinChanged_EmitsCriticalAction()
    {
        // Arrange — existing config with one thumbprint set
        var existing    = MakeConfig();
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(existing);

        // Simulate existing config having a thumbprint value via domain update
        existing.Update("remote-viettel-mysign", null, "AABBCC", OcspMode.Soft, true, SignatureFlowMode.Async, TestActorId);

        string? capturedAction = null;
        await auditLogger.LogCommandAsync(
            action:       Arg.Do<string>(a => capturedAction = a),
            moduleName:   Arg.Any<string>(),
            resourceType: Arg.Any<string>(),
            resourceId:   Arg.Any<string?>(),
            commandType:  Arg.Any<Type?>(),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());

        var handler = new UpdateTenantSignatureConfigCommandHandler(repo, auditLogger);
        // New thumbprints differ from old — root pin change
        var cmd = new UpdateTenantSignatureConfigCommand(
            TestTenantId, "remote-viettel-mysign", null, "DDEEFF",
            OcspMode.Soft, true, SignatureFlowMode.Async, TestActorId);

        await handler.Handle(cmd, default);

        capturedAction.Should().Be("CA_ROOT_THUMBPRINTS_CHANGED",
            because: "changing AllowedCaRootThumbprints must emit Critical-severity audit action per QĐ 742 §5.1");
    }

    // ── SignPayloadCommandHandler ──────────────────────────────────────────────

    [Fact]
    public async Task SignPayload_NoConfig_ReturnsNotFound()
    {
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns((TenantSignatureConfig?)null);

        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(), MakeSignerBindingGuard());
        var result  = await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, "user1", null, null), default);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(TestTenantId.ToString()));
    }

    [Fact]
    public async Task SignPayload_SigningDisabled_ReturnsConflict()
    {
        var config      = MakeConfig(isEnabled: false);
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(), MakeSignerBindingGuard());
        var result  = await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, "user1", null, null), default);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("disabled"));
    }

    [Fact]
    public async Task SignPayload_ClientSideProvider_ReturnsConflict()
    {
        var config      = MakeConfig(providerKey: "usb-vgca");
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var usbProvider = Substitute.For<IDigitalSignatureProvider>();
        usbProvider.Capabilities.Returns(
            SignatureProviderCapabilities.ClientSideSigning | SignatureProviderCapabilities.RequiresLocalAgent);
        resolver.Resolve("usb-vgca").Returns(usbProvider);

        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(), MakeSignerBindingGuard());
        var result  = await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, "user1", null, null), default);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("client-side signing"));
    }

    [Fact]
    public async Task SignPayload_ServerSideProvider_ReturnsSignResult()
    {
        var config      = MakeConfig(providerKey: "remote-viettel-mysign");
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Any<SignRequest>(), default)
            .Returns(new SignResult(true, TestSignature, null, null));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());
        var result  = await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSuccess.Should().BeTrue();
        result.Value.Signature.Should().BeEquivalentTo(TestSignature);
    }

    // A cert-bearing sign whose certificate is NOT bound to the signer must fail-closed with
    // SIGNER_CERT_MISMATCH — the anti-impersonation baseline for normal signing.
    [Fact]
    public async Task SignPayload_BindingGuardRejects_ReturnsCertMismatch()
    {
        var certInfo = new CertificateInfo(
            SerialNumber: "9710405", Subject: "CN=Mysign User", Issuer: "CN=Viettel CA",
            NotBefore: DateTime.UtcNow.AddDays(-1), NotAfter: DateTime.UtcNow.AddDays(365),
            Thumbprint: "DEADBEEF", RawData: []);
        var config   = MakeConfig(providerKey: "remote-viettel-mysign");
        var repo     = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver = Substitute.For<ISignatureProviderResolver>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Any<SignRequest>(), default)
            .Returns(new SignResult(true, TestSignature, certInfo, null));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        var guard = Substitute.For<ISignerBindingGuard>();
        guard.VerifyAsync(Arg.Any<CertificateInfo>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(SignerBindingVerdict.NotBound("no approved enrollment"));

        var signer  = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, Substitute.For<IAuditLogger>(), MakeUserManager(signer), guard);
        var result  = await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null), default);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("ErrorCode") && ((string?)e.Metadata["ErrorCode"]) == "SIGNER_CERT_MISMATCH");
    }

    // SkipBindingGuard (enrollment possession-proof path): the binding guard is bypassed so a
    // not-yet-enrolled cert still returns success — proving possession is what CREATES the binding,
    // so requiring an existing binding would be circular. The guard must not even be invoked.
    [Fact]
    public async Task SignPayload_SkipBindingGuard_BypassesRejectionAndReturnsSuccess()
    {
        var certInfo = new CertificateInfo(
            SerialNumber: "9710405", Subject: "CN=Mysign User", Issuer: "CN=Viettel CA",
            NotBefore: DateTime.UtcNow.AddDays(-1), NotAfter: DateTime.UtcNow.AddDays(365),
            Thumbprint: "DEADBEEF", RawData: []);
        var config   = MakeConfig(providerKey: "remote-viettel-mysign");
        var repo     = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver = Substitute.For<ISignatureProviderResolver>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Any<SignRequest>(), default)
            .Returns(new SignResult(true, TestSignature, certInfo, null));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        // A guard that WOULD reject — proving the skip path never consults it.
        var guard = Substitute.For<ISignerBindingGuard>();
        guard.VerifyAsync(Arg.Any<CertificateInfo>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(SignerBindingVerdict.NotBound("no approved enrollment"));

        var signer  = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, Substitute.For<IAuditLogger>(), MakeUserManager(signer), guard);
        var result  = await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null, SkipBindingGuard: true), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSuccess.Should().BeTrue();
        result.Value.SigningCertificate!.SerialNumber.Should().Be("9710405");
        await guard.DidNotReceive().VerifyAsync(
            Arg.Any<CertificateInfo>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignPayload_Success_EmitsAuditWithPayloadHashAndCertFields()
    {
        var certInfo = new CertificateInfo(
            SerialNumber: "1A2B3C",
            Subject:      "CN=TestUser",
            Issuer:       "CN=TestCA",
            NotBefore:    DateTime.UtcNow.AddDays(-1),
            NotAfter:     DateTime.UtcNow.AddDays(365),
            Thumbprint:   "AABBCCDD",
            RawData:      []);

        var config   = MakeConfig(providerKey: "remote-viettel-mysign");
        var repo     = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Any<SignRequest>(), default)
            .Returns(new SignResult(true, TestSignature, certInfo, null));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        string? capturedSnapshot = null;
        await auditLogger.LogCommandAsync(
            action:       Arg.Any<string>(),
            moduleName:   Arg.Any<string>(),
            resourceType: Arg.Any<string>(),
            resourceId:   Arg.Any<string?>(),
            commandType:  Arg.Any<Type?>(),
            dataSnapshot: Arg.Do<string?>(s => capturedSnapshot = s),
            cancellationToken: Arg.Any<CancellationToken>());

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());
        await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null), default);

        capturedSnapshot.Should().NotBeNull();
        capturedSnapshot.Should().Contain("1A2B3C",  because: "certSerial must be in dataSnapshot");
        capturedSnapshot.Should().Contain("CN=TestUser", because: "certSubject must be in dataSnapshot");
        capturedSnapshot.Should().Contain("payloadHash", because: "SHA-256 payloadHash must be in dataSnapshot");
        capturedSnapshot.Should().Contain("remote-viettel-mysign", because: "providerKey must be in dataSnapshot");
        capturedSnapshot.Should().Contain("\"outcome\":\"success\"",
            because: "outcome must reflect successful signing result");

        // Verify payloadHash value matches SHA-256 of TestPayload
        var expectedHash = Convert.ToHexString(SHA256.HashData(TestPayload)).ToLowerInvariant();
        capturedSnapshot.Should().Contain(expectedHash);
    }

    [Fact]
    public async Task SignPayload_Failure_EmitsFailedAuditAction()
    {
        var config   = MakeConfig(providerKey: "remote-viettel-mysign");
        var repo     = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Any<SignRequest>(), default)
            .Returns(new SignResult(false, null, null, "Provider timeout"));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        string? capturedAction = null;
        await auditLogger.LogCommandAsync(
            action:       Arg.Do<string>(a => capturedAction = a),
            moduleName:   Arg.Any<string>(),
            resourceType: Arg.Any<string>(),
            resourceId:   Arg.Any<string?>(),
            commandType:  Arg.Any<Type?>(),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());
        await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null), default);

        capturedAction.Should().Be("SIGN_PAYLOAD_FAILED",
            because: "provider failure must emit a distinct action for ops triage");
    }

    [Fact]
    public async Task SignPayload_UserHasCccd_PassesCccdToProviderAndKeepsGuidInAudit()
    {
        var config   = MakeConfig(providerKey: "remote-viettel-mysign");
        var repo     = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        SignRequest? captured = null;
        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Do<SignRequest>(r => captured = r), default)
            .Returns(new SignResult(true, TestSignature, null, null));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        string? snapshot = null;
        await auditLogger.LogCommandAsync(
            action:       Arg.Any<string>(),
            moduleName:   Arg.Any<string>(),
            resourceType: Arg.Any<string>(),
            resourceId:   Arg.Any<string?>(),
            commandType:  Arg.Any<Type?>(),
            dataSnapshot: Arg.Do<string?>(s => snapshot = s),
            cancellationToken: Arg.Any<CancellationToken>());

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());

        await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null), default);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be("017091004878",
            because: "the provider must receive the signer's CCCD as the CA-side user_id, not the app Guid");

        snapshot.Should().Contain(signer.Id.ToString(),
            because: "the audit actor stays the internal user Guid");
        snapshot.Should().NotContain("017091004878",
            because: "the CCCD is PII and must NOT be written to the audit snapshot");
    }

    [Fact]
    public async Task SignPayload_UserWithoutCccd_FailsClosedWithSignerIdentityUnresolved()
    {
        var config   = MakeConfig(providerKey: "remote-viettel-mysign");
        var repo     = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = null };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());

        var result = await handler.Handle(
            new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null), default);

        result.IsFailed.Should().BeTrue(
            because: "a user without a CCCD must fail-closed (anti-impersonation: cannot attribute signature to wrong user)");
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("ErrorCode") && ((string?)e.Metadata["ErrorCode"]) == "SIGNER_IDENTITY_UNRESOLVED",
            because: "error code must clearly identify the identity resolution failure");
        // Verify provider was never called
        await remoteProvider.DidNotReceive().SignAsync(Arg.Any<SignRequest>(), Arg.Any<CancellationToken>());
    }

    // ── VerifySignatureCommandHandler ─────────────────────────────────────────

    [Fact]
    public async Task VerifySignature_NoConfig_ReturnsNotFound()
    {
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns((TenantSignatureConfig?)null);

        var handler = new VerifySignatureCommandHandler(repo, resolver, auditLogger);
        var result  = await handler.Handle(
            new VerifySignatureCommand(TestTenantId, TestPayload, TestSignature, null), default);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task VerifySignature_UsesOcspModeOverride_WhenProvided()
    {
        var config      = MakeConfig(); // default OcspMode.Soft
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        VerifyRequest? capturedRequest = null;
        var provider = Substitute.For<IDigitalSignatureProvider>();
        provider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        provider.VerifyAsync(Arg.Do<VerifyRequest>(r => capturedRequest = r), default)
            .Returns(new VerifyResult(true, null, null, null));
        resolver.Resolve("remote-viettel-mysign").Returns(provider);

        var handler = new VerifySignatureCommandHandler(repo, resolver, auditLogger);
        // Override to Hard even though tenant config is Soft
        await handler.Handle(
            new VerifySignatureCommand(TestTenantId, TestPayload, TestSignature, OcspMode.Hard), default);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.OcspMode.Should().Be(OcspMode.Hard,
            "override should win over tenant config OcspMode.Soft");
    }

    [Fact]
    public async Task VerifySignature_UsesTenantOcspMode_WhenNoOverride()
    {
        var config      = MakeConfig(); // OcspMode.Soft
        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        VerifyRequest? capturedRequest = null;
        var provider = Substitute.For<IDigitalSignatureProvider>();
        provider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        provider.VerifyAsync(Arg.Do<VerifyRequest>(r => capturedRequest = r), default)
            .Returns(new VerifyResult(true, null, null, null));
        resolver.Resolve("remote-viettel-mysign").Returns(provider);

        var handler = new VerifySignatureCommandHandler(repo, resolver, auditLogger);
        await handler.Handle(
            new VerifySignatureCommand(TestTenantId, TestPayload, TestSignature, null), default);

        capturedRequest!.OcspMode.Should().Be(OcspMode.Soft,
            "null override should fall back to tenant config OcspMode.Soft");
    }

    [Fact]
    public async Task VerifySignature_Valid_EmitsAuditWithCertAndOcspFields()
    {
        var certInfo = new CertificateInfo(
            SerialNumber: "CERT001",
            Subject:      "CN=Signer",
            Issuer:       "CN=VNca",
            NotBefore:    DateTime.UtcNow.AddDays(-1),
            NotAfter:     DateTime.UtcNow.AddDays(365),
            Thumbprint:   "FFEE",
            RawData:      []);

        var chainResult = new ChainValidationResult(
            IsValid:      true,
            FailureReason: null,
            Chain:        [],
            OcspChecked:  true,
            OcspSucceeded: true);

        var config   = MakeConfig();
        var repo     = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var provider = Substitute.For<IDigitalSignatureProvider>();
        provider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        provider.VerifyAsync(Arg.Any<VerifyRequest>(), default)
            .Returns(new VerifyResult(true, null, certInfo, chainResult));
        resolver.Resolve("remote-viettel-mysign").Returns(provider);

        string? capturedSnapshot = null;
        string? capturedAction   = null;
        await auditLogger.LogCommandAsync(
            action:       Arg.Do<string>(a => capturedAction = a),
            moduleName:   Arg.Any<string>(),
            resourceType: Arg.Any<string>(),
            resourceId:   Arg.Any<string?>(),
            commandType:  Arg.Any<Type?>(),
            dataSnapshot: Arg.Do<string?>(s => capturedSnapshot = s),
            cancellationToken: Arg.Any<CancellationToken>());

        var handler = new VerifySignatureCommandHandler(repo, resolver, auditLogger);
        await handler.Handle(
            new VerifySignatureCommand(TestTenantId, TestPayload, TestSignature, null), default);

        capturedAction.Should().Be("SIGNATURE_VERIFIED");
        capturedSnapshot.Should().Contain("CERT001",     because: "certSerial must be in dataSnapshot");
        capturedSnapshot.Should().Contain("CN=Signer",   because: "certSubject must be in dataSnapshot");
        capturedSnapshot.Should().Contain("payloadHash", because: "SHA-256 payloadHash must be in dataSnapshot");
        capturedSnapshot.Should().Contain("signatureHash", because: "SHA-256 signatureHash must be in dataSnapshot");
        capturedSnapshot.Should().Contain("\"ocspChecked\":true");
        capturedSnapshot.Should().Contain("\"ocspSucceeded\":true");
        capturedSnapshot.Should().Contain("\"outcome\":\"valid\"");
    }

    [Fact]
    public async Task VerifySignature_Invalid_EmitsVerificationFailedAction()
    {
        var config   = MakeConfig();
        var repo     = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        var provider = Substitute.For<IDigitalSignatureProvider>();
        provider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        provider.VerifyAsync(Arg.Any<VerifyRequest>(), default)
            .Returns(new VerifyResult(false, "CertExpired", null, null));
        resolver.Resolve("remote-viettel-mysign").Returns(provider);

        string? capturedAction = null;
        await auditLogger.LogCommandAsync(
            action:       Arg.Do<string>(a => capturedAction = a),
            moduleName:   Arg.Any<string>(),
            resourceType: Arg.Any<string>(),
            resourceId:   Arg.Any<string?>(),
            commandType:  Arg.Any<Type?>(),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());

        var handler = new VerifySignatureCommandHandler(repo, resolver, auditLogger);
        await handler.Handle(
            new VerifySignatureCommand(TestTenantId, TestPayload, TestSignature, null), default);

        capturedAction.Should().Be("SIGNATURE_VERIFICATION_FAILED",
            because: "invalid signature must emit a distinct action for forensic review");
    }

    // ── EnrollSignerCertCommandHandler ────────────────────────────────────────

    private static EnrollSignerCertCommand MakeEnrollCommand(Guid userId) =>
        new(
            TenantId:     TestTenantId,
            UserId:       userId,
            SerialNumber: "1A2B3C",
            Issuer:       "CN=TestCA",
            Thumbprint:   "AABBCCDD",
            Subject:      "CN=TestSigner",
            EnrolledBy:   TestActorId);

    [Fact]
    public async Task EnrollCert_UserExistsInTenant_AddsEnrollment()
    {
        var userId = Guid.NewGuid();
        var signer = new ApplicationUser { Id = userId, TenantId = TestTenantId };
        var repo   = Substitute.For<ISignerCertEnrollmentRepository>();
        repo.GetActiveByUserAsync(TestTenantId, userId, Arg.Any<CancellationToken>())
            .Returns([]);

        var handler = new EnrollSignerCertCommandHandler(repo, MakeUserManager(signer), Gen);
        var result  = await handler.Handle(MakeEnrollCommand(userId), default);

        result.IsSuccess.Should().BeTrue();
        await repo.Received(1).AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrollCert_UserNotFound_ReturnsNotFoundAndDoesNotInsert()
    {
        var userId = Guid.NewGuid();
        var repo   = Substitute.For<ISignerCertEnrollmentRepository>();

        // MakeUserManager() with no user → FindByIdAsync returns null.
        var handler = new EnrollSignerCertCommandHandler(repo, MakeUserManager(), Gen);
        var result  = await handler.Handle(MakeEnrollCommand(userId), default);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("ErrorCode") && ((string?)e.Metadata["ErrorCode"]) == "GOV_SYS_001",
            because: "a missing signer must surface as NotFound (404), not a silent orphan insert");
        await repo.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrollCert_UserInDifferentTenant_ReturnsNotFoundAndDoesNotInsert()
    {
        var userId = Guid.NewGuid();
        // Signer exists but is scoped to another tenant — must be indistinguishable from "not found".
        var foreignSigner = new ApplicationUser { Id = userId, TenantId = Guid.NewGuid() };
        var repo = Substitute.For<ISignerCertEnrollmentRepository>();

        var handler = new EnrollSignerCertCommandHandler(repo, MakeUserManager(foreignSigner), Gen);
        var result  = await handler.Handle(MakeEnrollCommand(userId), default);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("ErrorCode") && ((string?)e.Metadata["ErrorCode"]) == "GOV_SYS_001",
            because: "enrolling a cross-tenant signer must fail-closed without leaking that the user exists elsewhere");
        await repo.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrollCert_GlobalUserWithNullTenant_ReturnsNotFoundAndDoesNotInsert()
    {
        var userId = Guid.NewGuid();
        // A global/system user has TenantId == null and cannot be a tenant-scoped signer.
        var globalUser = new ApplicationUser { Id = userId, TenantId = null };
        var repo = Substitute.For<ISignerCertEnrollmentRepository>();

        var handler = new EnrollSignerCertCommandHandler(repo, MakeUserManager(globalUser), Gen);
        var result  = await handler.Handle(MakeEnrollCommand(userId), default);

        result.IsFailed.Should().BeTrue(
            because: "a user with no tenant scope must not be enrollable into a specific tenant");
        await repo.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrollCert_DuplicateActiveCert_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var signer = new ApplicationUser { Id = userId, TenantId = TestTenantId };
        var existing = SignerCertEnrollment.Create(
            tenantId:       TestTenantId,
            userId:         userId,
            serialNumber:   "1A2B3C",
            issuer:         "CN=TestCA",
            thumbprint:     "AABBCCDD",
            subject:        "CN=TestSigner",
            enrolledBy:     TestActorId,
            kind:           EnrollmentKind.RemoteViettelMysign,
            approvalStatus: CertApprovalStatus.Approved,
            subjectType:    CertSubjectType.Personal,
            displayName:    "TestSigner",
            gen:            Gen);
        var repo = Substitute.For<ISignerCertEnrollmentRepository>();
        repo.GetActiveByUserAsync(TestTenantId, userId, Arg.Any<CancellationToken>())
            .Returns([existing]);

        var handler = new EnrollSignerCertCommandHandler(repo, MakeUserManager(signer), Gen);
        var result  = await handler.Handle(MakeEnrollCommand(userId), default);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("ErrorCode") && ((string?)e.Metadata["ErrorCode"]) == "SIGNER_CERT_ALREADY_ENROLLED",
            because: "the existing duplicate guard must still fire after the user-existence check");
        await repo.DidNotReceive().AddAsync(Arg.Any<SignerCertEnrollment>(), Arg.Any<CancellationToken>());
    }

    // ── SignPayloadCommand: Phase 01 async=3 callback wire-up ─────────────────

    [Fact]
    public async Task SignPayload_TenantAsyncCallbackMode_InjectsSignFlowModeAndUserIdIntoExtraParameters()
    {
        // Arrange: tenant with AsyncCallback flow mode
        var config = TenantSignatureConfig.Create(
            tenantId:                  TestTenantId,
            providerKey:               "remote-viettel-mysign",
            apiCredentialsRef:         null,
            allowedCaRootThumbprints:  string.Empty,
            ocspMode:                  OcspMode.Soft,
            isEnabled:                 true,
            signFlowMode:              SignatureFlowMode.AsyncCallback, // ← async=3 mode
            createdBy:                 TestActorId);

        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        SignRequest? capturedRequest = null;
        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Do<SignRequest>(r => capturedRequest = r), default)
            .Returns(new SignResult(true, null, null, null, "tx-123", true));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());

        // Act: sign with no caller-supplied ExtraParameters
        var cmd = new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null);
        var result = await handler.Handle(cmd, default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();

        // Verify server-set ExtraParameters: tenantId and userId always injected
        capturedRequest!.ExtraParameters.Should().NotBeNull();
        capturedRequest.ExtraParameters!.Should().ContainKey("tenantId")
            .WhoseValue.Should().Be(TestTenantId.ToString());
        capturedRequest.ExtraParameters.Should().ContainKey("userId")
            .WhoseValue.Should().Be(signer.Id.ToString());

        // Verify signFlowMode is injected ONLY for AsyncCallback mode
        capturedRequest.ExtraParameters.Should().ContainKey("signFlowMode")
            .WhoseValue.Should().Be(nameof(SignatureFlowMode.AsyncCallback),
                because: "AsyncCallback mode must inject signFlowMode so provider maps to async=3");
    }

    [Fact]
    public async Task SignPayload_ClientSuppliedControlKeys_AreStrippedNotHonored()
    {
        // Anti-bypass: a client must NOT be able to force the signing mode (admin-controlled) or spoof the
        // signer/tenant identity by smuggling reserved keys through the public /sign ExtraParameters bag.
        // The handler strips reserved control keys before setting its authoritative server values.
        var config = TenantSignatureConfig.Create(
            tenantId:                  TestTenantId,
            providerKey:               "remote-viettel-mysign",
            apiCredentialsRef:         null,
            allowedCaRootThumbprints:  string.Empty,
            ocspMode:                  OcspMode.Soft,
            isEnabled:                 true,
            signFlowMode:              SignatureFlowMode.AsyncCallback,
            createdBy:                 TestActorId);

        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        SignRequest? capturedRequest = null;
        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Do<SignRequest>(r => capturedRequest = r), default)
            .Returns(new SignResult(true, null, null, null, "tx-strip", true));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());

        // Client attempts to force Sync mode + spoof identity through the public body.
        var clientParams = new Dictionary<string, string>
        {
            ["mysignMode"]   = "Sync",                       // force-mode attempt (highest ResolveMode precedence)
            ["signFlowMode"] = nameof(SignatureFlowMode.Sync), // force-mode attempt
            ["userId"]       = Guid.NewGuid().ToString(),     // identity spoof attempt
            ["tenantId"]     = Guid.NewGuid().ToString(),     // tenant spoof attempt
            ["documentId"]   = "doc-keep",                    // benign correlation — must be preserved
        };
        var cmd = new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, clientParams);
        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();

        // mysignMode stripped — the client cannot bypass admin mode-control (ResolveMode honors it first).
        capturedRequest!.ExtraParameters!.Should().NotContainKey("mysignMode",
            because: "a client-supplied mysignMode must be stripped so it cannot override admin mode-control");
        // signFlowMode is the SERVER's value (AsyncCallback from tenant config), NOT the client's "Sync".
        capturedRequest.ExtraParameters["signFlowMode"].Should().Be(nameof(SignatureFlowMode.AsyncCallback));
        // Identity is server-set from the authenticated command, NOT the client's spoofed values.
        capturedRequest.ExtraParameters["userId"].Should().Be(signer.Id.ToString());
        capturedRequest.ExtraParameters["tenantId"].Should().Be(TestTenantId.ToString());
        // Benign caller correlation is preserved through the copy.
        capturedRequest.ExtraParameters.Should().ContainKey("documentId")
            .WhoseValue.Should().Be("doc-keep");
    }

    [Fact]
    public async Task SignPayload_TenantAsyncMode_DoesNotInjectSignFlowMode()
    {
        // Arrange: tenant with default Async flow mode
        var config = TenantSignatureConfig.Create(
            tenantId:                  TestTenantId,
            providerKey:               "remote-viettel-mysign",
            apiCredentialsRef:         null,
            allowedCaRootThumbprints:  string.Empty,
            ocspMode:                  OcspMode.Soft,
            isEnabled:                 true,
            signFlowMode:              SignatureFlowMode.Async, // ← async=2 default
            createdBy:                 TestActorId);

        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        SignRequest? capturedRequest = null;
        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Do<SignRequest>(r => capturedRequest = r), default)
            .Returns(new SignResult(true, null, null, null, "tx-456", true));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());

        // Act
        var cmd = new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null);
        var result = await handler.Handle(cmd, default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();

        // Verify tenantId and userId are always present
        capturedRequest!.ExtraParameters.Should().NotBeNull();
        capturedRequest.ExtraParameters!.Should().ContainKey("tenantId");
        capturedRequest.ExtraParameters.Should().ContainKey("userId");

        // Verify signFlowMode is NOT injected for default Async mode
        // (regression guard: the generic sign path should keep DefaultMode when not explicitly set)
        capturedRequest.ExtraParameters.Should().NotContainKey("signFlowMode",
            because: "Async mode (default) must NOT override provider DefaultMode silently");
    }

    [Fact]
    public async Task SignPayload_TenantSyncMode_DoesNotInjectSignFlowMode()
    {
        // Arrange: tenant with Sync flow mode
        var config = TenantSignatureConfig.Create(
            tenantId:                  TestTenantId,
            providerKey:               "remote-viettel-mysign",
            apiCredentialsRef:         null,
            allowedCaRootThumbprints:  string.Empty,
            ocspMode:                  OcspMode.Soft,
            isEnabled:                 true,
            signFlowMode:              SignatureFlowMode.Sync, // ← async=0 blocking
            createdBy:                 TestActorId);

        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        SignRequest? capturedRequest = null;
        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Do<SignRequest>(r => capturedRequest = r), default)
            .Returns(new SignResult(true, TestSignature, null, null));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());

        // Act
        var cmd = new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, null);
        var result = await handler.Handle(cmd, default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();

        // Verify tenantId and userId are present
        capturedRequest!.ExtraParameters.Should().NotBeNull();
        capturedRequest.ExtraParameters!.Should().ContainKey("tenantId");
        capturedRequest.ExtraParameters.Should().ContainKey("userId");

        // Verify signFlowMode is NOT injected for Sync mode (regression guard)
        capturedRequest.ExtraParameters.Should().NotContainKey("signFlowMode",
            because: "Sync mode must NOT override provider DefaultMode silently");
    }

    [Fact]
    public async Task SignPayload_CallerExtraParametersPreserved_WithServerSetFields()
    {
        // Arrange: tenant with AsyncCallback, and caller supplies extra parameters
        var config = TenantSignatureConfig.Create(
            tenantId:                  TestTenantId,
            providerKey:               "remote-viettel-mysign",
            apiCredentialsRef:         null,
            allowedCaRootThumbprints:  string.Empty,
            ocspMode:                  OcspMode.Soft,
            isEnabled:                 true,
            signFlowMode:              SignatureFlowMode.AsyncCallback,
            createdBy:                 TestActorId);

        var repo        = Substitute.For<ITenantSignatureConfigRepository>();
        var resolver    = Substitute.For<ISignatureProviderResolver>();
        var auditLogger = Substitute.For<IAuditLogger>();
        repo.GetByTenantIdAsync(TestTenantId, default).Returns(config);

        SignRequest? capturedRequest = null;
        var remoteProvider = Substitute.For<IDigitalSignatureProvider>();
        remoteProvider.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        remoteProvider.SignAsync(Arg.Do<SignRequest>(r => capturedRequest = r), default)
            .Returns(new SignResult(true, null, null, null, "tx-789", true));
        resolver.Resolve("remote-viettel-mysign").Returns(remoteProvider);

        var signer = new ApplicationUser { Id = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var handler = new SignPayloadCommandHandler(repo, resolver, auditLogger, MakeUserManager(signer), MakeSignerBindingGuard());

        // Act: provide caller-supplied extra parameters
        var callerParams = new Dictionary<string, string>
        {
            ["requestId"] = "req-caller-123",
            ["documentId"] = "doc-456",
        };
        var cmd = new SignPayloadCommand(TestTenantId, TestPayload, signer.Id.ToString(), null, callerParams);
        var result = await handler.Handle(cmd, default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();

        // Verify caller params are preserved
        capturedRequest!.ExtraParameters.Should().NotBeNull();
        capturedRequest.ExtraParameters!.Should().ContainKey("requestId")
            .WhoseValue.Should().Be("req-caller-123");
        capturedRequest.ExtraParameters.Should().ContainKey("documentId")
            .WhoseValue.Should().Be("doc-456");

        // Verify server-set fields are present (always injected)
        capturedRequest.ExtraParameters.Should().ContainKey("userId");
        capturedRequest.ExtraParameters.Should().ContainKey("tenantId");
        capturedRequest.ExtraParameters.Should().ContainKey("signFlowMode");
    }
}
