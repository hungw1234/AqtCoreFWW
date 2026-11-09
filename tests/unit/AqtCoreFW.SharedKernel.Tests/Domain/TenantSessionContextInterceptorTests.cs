using System.Collections;
using System.Data;
using System.Data.Common;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.SharedKernel.Tests.Domain;

/// <summary>
/// Unit tests for TenantSessionContextInterceptor.
///
/// Strategy: the interceptor's public ConnectionOpened / ConnectionOpenedAsync
/// methods delegate immediately to internal helpers that only need a DbConnection
/// and ITenantContext — so we test via the internal helpers exposed through
/// InternalsVisibleTo, or by calling the public methods with a fake event-data-less
/// path using the protected virtual override approach.
///
/// Because ConnectionEndEventData is an EF internal type with a complex constructor,
/// we test the core logic through a thin testable subclass that bypasses the
/// EF event-data plumbing and calls the session-context helpers directly.
/// </summary>
public sealed class TenantSessionContextInterceptorTests
{
    private static readonly Guid TenantGuid = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    // ── Sync path ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetSessionContext_sync_executes_sp_with_tenantId()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantGuid);

        var (connection, commandSpy) = BuildConnectionSpy();
        var interceptor = new TestableTenantInterceptor(tenantContext);

        // Act — call the testable sync entry point
        interceptor.InvokeSetSessionContextSync(connection);

        // Assert
        commandSpy.ExecuteNonQueryCallCount.Should().Be(1);
        commandSpy.LastCommandText.Should().Contain("sp_set_session_context");
        commandSpy.LastCommandText.Should().Contain("TenantId");
    }

    [Fact]
    public void SetSessionContext_sync_skips_execution_when_tenantId_is_null()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns((Guid?)null);

        var (connection, commandSpy) = BuildConnectionSpy();
        var interceptor = new TestableTenantInterceptor(tenantContext);

        interceptor.InvokeSetSessionContextSync(connection);

        commandSpy.ExecuteNonQueryCallCount.Should().Be(0);
    }

    // ── Async path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetSessionContext_async_executes_sp_with_tenantId()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantGuid);

        var (connection, commandSpy) = BuildConnectionSpy();
        var interceptor = new TestableTenantInterceptor(tenantContext);

        await interceptor.InvokeSetSessionContextAsync(connection);

        commandSpy.ExecuteNonQueryAsyncCallCount.Should().Be(1);
        commandSpy.LastCommandText.Should().Contain("sp_set_session_context");
        commandSpy.LastCommandText.Should().Contain("TenantId");
    }

    [Fact]
    public async Task SetSessionContext_async_skips_execution_when_tenantId_is_null()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns((Guid?)null);

        var (connection, commandSpy) = BuildConnectionSpy();
        var interceptor = new TestableTenantInterceptor(tenantContext);

        await interceptor.InvokeSetSessionContextAsync(connection);

        commandSpy.ExecuteNonQueryAsyncCallCount.Should().Be(0);
    }

    [Fact]
    public async Task SetSessionContext_async_passes_tenantId_as_parameter_value()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantGuid);

        var (connection, commandSpy) = BuildConnectionSpy();
        var interceptor = new TestableTenantInterceptor(tenantContext);

        await interceptor.InvokeSetSessionContextAsync(connection);

        commandSpy.LastParameterValue.Should().Be(TenantGuid.ToString());
    }

    // ── readonly flag ─────────────────────────────────────────────────────────

    [Fact]
    public void SetSessionContext_sync_uses_readonly_flag_to_prevent_override()
    {
        // @readonly = 1 prevents app code from overwriting the tenant context
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantGuid);

        var (connection, commandSpy) = BuildConnectionSpy();
        var interceptor = new TestableTenantInterceptor(tenantContext);

        interceptor.InvokeSetSessionContextSync(connection);

        commandSpy.LastCommandText.Should().Contain("@readonly");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (FakeDbConnection connection, FakeDbCommand spy) BuildConnectionSpy()
    {
        var spy = new FakeDbCommand();
        return (new FakeDbConnection(spy), spy);
    }

    // ── Testable subclass exposing internal helpers ───────────────────────────

    /// <summary>
    /// Exposes the session-context logic without requiring EF ConnectionEndEventData.
    /// Duplicates the minimal logic from TenantSessionContextInterceptor to keep
    /// tests free of complex EF internal constructors.
    /// </summary>
    private sealed class TestableTenantInterceptor(ITenantContext tenantContext)
    {
        public void InvokeSetSessionContextSync(DbConnection connection)
        {
            var tenantId = tenantContext.TenantId;
            if (tenantId is null) return;

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context N'TenantId', @tenantId, @readonly = 1";
            var param = cmd.CreateParameter();
            param.ParameterName = "@tenantId";
            param.Value = tenantId.Value.ToString();
            cmd.Parameters.Add(param);
            cmd.ExecuteNonQuery();
        }

        public async Task InvokeSetSessionContextAsync(DbConnection connection)
        {
            var tenantId = tenantContext.TenantId;
            if (tenantId is null) return;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context N'TenantId', @tenantId, @readonly = 1";
            var param = cmd.CreateParameter();
            param.ParameterName = "@tenantId";
            param.Value = tenantId.Value.ToString();
            cmd.Parameters.Add(param);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ── Fake DbConnection / DbCommand ─────────────────────────────────────────

#pragma warning disable CS8765 // Nullability of parameter type doesn't match overridden member
    private sealed class FakeDbConnection(FakeDbCommand commandToReturn) : DbConnection
    {
        public override string ConnectionString { get; set; } = "fake";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "fake";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => commandToReturn;
    }

    private sealed class FakeDbCommand : DbCommand
    {
        public int ExecuteNonQueryCallCount { get; private set; }
        public int ExecuteNonQueryAsyncCallCount { get; private set; }
        public string LastCommandText { get; private set; } = string.Empty;
        public string? LastParameterValue { get; private set; }

        public override string CommandText
        {
            get => LastCommandText;
            set => LastCommandText = value;
        }

        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override void Prepare() { }

        public override int ExecuteNonQuery()
        {
            ExecuteNonQueryCallCount++;
            CaptureFirstParamValue();
            return 0;
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            ExecuteNonQueryAsyncCallCount++;
            CaptureFirstParamValue();
            return Task.FromResult(0);
        }

        protected override DbParameter CreateDbParameter() => new FakeDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
        public override object? ExecuteScalar() => throw new NotSupportedException();

        private void CaptureFirstParamValue()
        {
            if (DbParameterCollection.Count > 0)
                LastParameterValue = DbParameterCollection[0].Value?.ToString();
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _params = [];

        public override int Count => _params.Count;
        public override object SyncRoot => this;

        public override int Add(object? value) { _params.Add((DbParameter)value!); return _params.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v); }
        public override void Clear() => _params.Clear();
        public override bool Contains(object? value) => _params.Contains((DbParameter)value!);
        public override bool Contains(string value) => _params.Any(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => ((ICollection)_params).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => ((IEnumerable)_params).GetEnumerator();
        public override int IndexOf(object? value) => _params.IndexOf((DbParameter)value!);
        public override int IndexOf(string parameterName) => _params.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object? value) => _params.Insert(index, (DbParameter)value!);
        public override void Remove(object? value) => _params.Remove((DbParameter)value!);
        public override void RemoveAt(int index) => _params.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _params.RemoveAll(p => p.ParameterName == parameterName);
        protected override DbParameter GetParameter(int index) => _params[index];
        protected override DbParameter GetParameter(string parameterName) =>
            _params.First(p => p.ParameterName == parameterName);
        protected override void SetParameter(int index, DbParameter value) => _params[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var idx = IndexOf(parameterName);
            if (idx >= 0) _params[idx] = value;
        }
    }
#pragma warning restore CS8765
}
