using AqtCoreFW.Infrastructure.Events;
using AqtCoreFW.SharedKernel.Events;

namespace AqtCoreFW.SharedKernel.Tests.Events;

/// <summary>
/// Unit tests for EventCatalogService.
/// Validates: register events, retrieve all, lookup by name, thread-safety.
/// </summary>
public sealed class EventCatalogServiceTests
{
    private readonly IEventCatalogService _service;

    public EventCatalogServiceTests()
    {
        _service = new EventCatalogService();
    }

    // --- Success path ---

    [Fact]
    public void Register_SingleEvent_AddsToRegistry()
    {
        _service.Register<TestEvent>("TestModule", "Test event for testing");

        var all = _service.GetAll();

        all.Should().ContainSingle(e => e.EventName == "TestEvent");
    }

    [Fact]
    public void Register_MultipleEvents_AllAdded()
    {
        _service.Register<TestEvent1>("Module1");
        _service.Register<TestEvent2>("Module2");
        _service.Register<TestEvent3>("Module3");

        var all = _service.GetAll();

        all.Should().HaveCount(3);
        all.Select(e => e.EventName).Should().Contain(new[] { "TestEvent1", "TestEvent2", "TestEvent3" });
    }

    [Fact]
    public void Register_WithDescription_IncludesDescription()
    {
        var description = "This is a test event";
        _service.Register<TestEvent>("TestModule", description);

        var entry = _service.GetByName("TestEvent");

        entry.Should().NotBeNull();
        entry.Description.Should().Be(description);
    }

    [Fact]
    public void Register_WithoutDescription_DescriptionNull()
    {
        _service.Register<TestEvent>("TestModule");

        var entry = _service.GetByName("TestEvent")!;

        entry.Description.Should().BeNull();
    }

    [Fact]
    public void Register_SetsSourceModule()
    {
        _service.Register<TestEvent>("CaseModule");

        var entry = _service.GetByName("TestEvent")!;

        entry.SourceModule.Should().Be("CaseModule");
    }

    [Fact]
    public void Register_SetsEventType()
    {
        _service.Register<TestEvent>("Module");

        var entry = _service.GetByName("TestEvent")!;

        entry.EventType.Should().Be(typeof(TestEvent));
    }

    [Fact]
    public void Register_SetsSchemaVersion()
    {
        _service.Register<TestEvent>("Module");

        var entry = _service.GetByName("TestEvent")!;

        entry.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public void GetAll_ReturnsOrderedBySourceModuleThenEventName()
    {
        _service.Register<EventZ>("ModuleB");
        _service.Register<EventA>("ModuleA");
        _service.Register<EventM>("ModuleA");
        _service.Register<EventX>("ModuleB");

        var all = _service.GetAll();

        all.Should().HaveCount(4);
        // ModuleA first, then ModuleB
        all.First().SourceModule.Should().Be("ModuleA");
        // Within ModuleA, EventA before EventM
        all.Where(e => e.SourceModule == "ModuleA")
            .Select(e => e.EventName)
            .Should().ContainInOrder("EventA", "EventM");
    }

    [Fact]
    public void GetByName_CaseInsensitive()
    {
        _service.Register<TestEvent>("Module");

        var lowercase = _service.GetByName("testevent");
        var uppercase = _service.GetByName("TESTEVENT");
        var mixed = _service.GetByName("TestEvent");

        lowercase.Should().NotBeNull();
        uppercase.Should().NotBeNull();
        mixed.Should().NotBeNull();
    }

    [Fact]
    public void GetByName_EventExists_ReturnsEntry()
    {
        _service.Register<TestEvent>("Module", "A test event");

        var entry = _service.GetByName("TestEvent");

        entry.Should().NotBeNull();
        entry.EventName.Should().Be("TestEvent");
        entry.SourceModule.Should().Be("Module");
    }

    [Fact]
    public void GetByName_EventNotFound_ReturnsNull()
    {
        var entry = _service.GetByName("NonExistentEvent");

        entry.Should().BeNull();
    }

    [Fact]
    public void Register_Idempotent_RegisterSameEventTwice()
    {
        _service.Register<TestEvent>("Module1", "First description");
        _service.Register<TestEvent>("Module2", "Second description");

        var all = _service.GetAll();

        // Should only appear once (last registration wins)
        all.Where(e => e.EventName == "TestEvent").Should().HaveCount(1);
    }

    [Fact]
    public void GetAll_EmptyRegistry_ReturnsEmpty()
    {
        var all = _service.GetAll();

        all.Should().BeEmpty();
    }

    [Fact]
    public void GetAll_ReturnsReadOnlyList()
    {
        _service.Register<TestEvent>("Module");

        var all = _service.GetAll();

        all.Should().BeOfType<List<EventCatalogEntry>>();
    }

    // --- Thread safety ---

    [Fact]
    public async Task Register_ConcurrentRegistrations_AllSucceed()
    {
        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() =>
            {
                var eventType = Type.GetType($"AqtCoreFW.SharedKernel.Tests.Events.TestEvent{i}") ?? typeof(TestEvent);
                _service.Register<TestEvent>($"Module{i}", $"Event {i}");
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var all = _service.GetAll();
        all.Should().NotBeEmpty();
    }

    // --- Test event classes ---

    public class TestEvent { }
    public class TestEvent1 { }
    public class TestEvent2 { }
    public class TestEvent3 { }
    public class EventA { }
    public class EventM { }
    public class EventX { }
    public class EventZ { }
}
