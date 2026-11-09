using AqtCoreFW.Collaboration.Application.Services;
using FluentAssertions;

namespace AqtCoreFW.Collaboration.Domain.Tests.Services;

/// <summary>
/// Unit tests for MentionParser static helper.
/// Verifies Guid extraction from @mention patterns in message content.
/// </summary>
public sealed class MentionParserTests
{
    private static readonly Guid UserId1 = Guid.NewGuid();
    private static readonly Guid UserId2 = Guid.NewGuid();

    [Fact]
    public void ExtractMentions_SingleMention_ReturnsOneGuid()
    {
        var content = $"Hello @{UserId1} how are you?";
        var result = MentionParser.ExtractMentions(content);
        result.Should().ContainSingle().Which.Should().Be(UserId1);
    }

    [Fact]
    public void ExtractMentions_MultipleMentions_ReturnsAllGuids()
    {
        var content = $"@{UserId1} and @{UserId2} please review.";
        var result = MentionParser.ExtractMentions(content);
        result.Should().HaveCount(2)
            .And.Contain(UserId1)
            .And.Contain(UserId2);
    }

    [Fact]
    public void ExtractMentions_DuplicateMention_ReturnsDistinct()
    {
        var content = $"@{UserId1} @{UserId1} duplicate mention.";
        var result = MentionParser.ExtractMentions(content);
        result.Should().ContainSingle().Which.Should().Be(UserId1);
    }

    [Fact]
    public void ExtractMentions_NoMentions_ReturnsEmpty()
    {
        var result = MentionParser.ExtractMentions("No mentions here.");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMentions_EmptyContent_ReturnsEmpty()
    {
        var result = MentionParser.ExtractMentions(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMentions_WhitespaceContent_ReturnsEmpty()
    {
        var result = MentionParser.ExtractMentions("   ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMentions_InvalidGuidFormat_NotExtracted()
    {
        // Plain @word is not a Guid-format mention
        var result = MentionParser.ExtractMentions("@username is not a guid mention.");
        result.Should().BeEmpty();
    }
}
