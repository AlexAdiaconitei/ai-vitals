using System.Text;
using AIVitals.AgentActivity;

namespace AIVitals.UnitTests;

public sealed class AgentActivityBridgeProtocolTests
{
    private const string SessionKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void A_schema_one_helper_still_reaches_a_newer_app()
    {
        var wire = AgentActivityBridgeProtocol.Serialize(Message(1, null));

        var message = AgentActivityBridgeProtocol.Deserialize(wire);

        Assert.NotNull(message);
        Assert.Null(message.WorkspaceLabel);
    }

    [Fact]
    public void A_schema_the_app_does_not_know_is_refused()
    {
        var wire = AgentActivityBridgeProtocol.Serialize(
            Message(AgentActivityBridgeProtocol.CurrentSchemaVersion + 1, "AI Vitals"));

        Assert.Null(AgentActivityBridgeProtocol.Deserialize(wire));
    }

    [Fact]
    public void A_well_formed_label_survives()
    {
        var wire = AgentActivityBridgeProtocol.Serialize(Message(2, "AI Vitals"));

        Assert.Equal("AI Vitals", AgentActivityBridgeProtocol.Deserialize(wire)!.WorkspaceLabel);
    }

    [Theory]
    [InlineData("D:/private/client")]
    [InlineData(@"D:\private\client")]
    [InlineData("C:client")]
    [InlineData("bell\u0007")]
    [InlineData("line\nbreak")]
    public void A_label_that_smuggles_a_path_or_control_character_is_dropped_but_the_event_survives(string label)
    {
        var wire = AgentActivityBridgeProtocol.Serialize(Message(2, label));

        var message = AgentActivityBridgeProtocol.Deserialize(wire);

        Assert.NotNull(message);
        Assert.Null(message.WorkspaceLabel);
        Assert.Equal(AgentActivityEvent.PromptSubmitted, message.Event);
        Assert.DoesNotContain(label, Encoding.UTF8.GetString(AgentActivityBridgeProtocol.Serialize(message)));
    }

    [Fact]
    public void An_overlong_label_is_dropped()
    {
        var label = new string('a', AgentActivityBridgeProtocol.MaximumWorkspaceLabelLength + 1);

        Assert.Null(AgentActivityBridgeProtocol.Deserialize(
            AgentActivityBridgeProtocol.Serialize(Message(2, label)))!.WorkspaceLabel);
    }

    private static AgentActivityWireMessage Message(int schemaVersion, string? label) => new(
        schemaVersion,
        AgentActivityProvider.ClaudeCode,
        AgentActivityEvent.PromptSubmitted,
        SessionKey,
        null,
        DateTimeOffset.UnixEpoch,
        label);
}
