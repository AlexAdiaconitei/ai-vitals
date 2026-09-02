using System.Text;
using AIVitals.AgentActivity;

namespace AIVitals.UnitTests;

public sealed class AgentActivityHookPayloadTests
{
    [Fact]
    public void Wire_payload_contains_only_pseudonymous_allowlisted_fields()
    {
        const string secretPrompt = "do not leak this prompt";
        const string secretPath = "C:/private/client/project";
        var input = Encoding.UTF8.GetBytes("""
            {
              "session_id": "session-sensitive-value",
              "tool_use_id": "tool-sensitive-value",
              "prompt": "do not leak this prompt",
              "cwd": "C:/private/client/project",
              "transcript_path": "C:/private/transcript.jsonl",
              "tool_input": { "command": "upload credentials" },
              "tool_response": "private result"
            }
            """);

        var wire = AgentActivityHookPayload.Project(
            input,
            AgentActivityProvider.ClaudeCode,
            AgentActivityEvent.ToolStarted,
            Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(),
            DateTimeOffset.UnixEpoch);

        Assert.NotNull(wire);
        var wireText = Encoding.UTF8.GetString(wire);
        Assert.DoesNotContain(secretPrompt, wireText);
        Assert.DoesNotContain(secretPath, wireText);
        Assert.DoesNotContain("session-sensitive-value", wireText);
        Assert.DoesNotContain("tool-sensitive-value", wireText);
        Assert.DoesNotContain("upload credentials", wireText);

        var message = AgentActivityBridgeProtocol.Deserialize(wire);
        Assert.NotNull(message);
        Assert.Equal(43, message.SessionKey.Length);
        Assert.Equal(43, message.ToolKey!.Length);
    }

    [Fact]
    public void Invalid_missing_or_oversized_payloads_are_ignored()
    {
        var key = new byte[32];
        Assert.Null(AgentActivityHookPayload.Project("not json"u8, AgentActivityProvider.Codex, AgentActivityEvent.TurnStopped, key, DateTimeOffset.UnixEpoch));
        Assert.Null(AgentActivityHookPayload.Project("{}"u8, AgentActivityProvider.Codex, AgentActivityEvent.TurnStopped, key, DateTimeOffset.UnixEpoch));
        Assert.Null(AgentActivityHookPayload.Project(new byte[AgentActivityBridgeProtocol.MaximumHookInputBytes + 1], AgentActivityProvider.Codex, AgentActivityEvent.TurnStopped, key, DateTimeOffset.UnixEpoch));
    }
    [Fact]
    public void The_workspace_label_is_the_leaf_folder_and_never_the_path()
    {
        var input = Encoding.UTF8.GetBytes("""
            {
              "session_id": "session-sensitive-value",
              "cwd": "D:/PROGRAMACION/VSCode-Workspace/AI Vitals"
            }
            """);

        var wire = AgentActivityHookPayload.Project(
            input,
            AgentActivityProvider.ClaudeCode,
            AgentActivityEvent.PromptSubmitted,
            new byte[32],
            DateTimeOffset.UnixEpoch,
            includeWorkspaceLabel: true);

        Assert.NotNull(wire);
        var wireText = Encoding.UTF8.GetString(wire);
        Assert.DoesNotContain("PROGRAMACION", wireText);
        Assert.DoesNotContain("VSCode-Workspace", wireText);
        Assert.Equal("AI Vitals", AgentActivityBridgeProtocol.Deserialize(wire)!.WorkspaceLabel);
    }

    [Fact]
    public void No_label_is_sent_unless_the_user_opted_in()
    {
        var input = Encoding.UTF8.GetBytes("""
            {
              "session_id": "session-sensitive-value",
              "cwd": "D:/clients/acme/project"
            }
            """);

        var wire = AgentActivityHookPayload.Project(
            input,
            AgentActivityProvider.ClaudeCode,
            AgentActivityEvent.PromptSubmitted,
            new byte[32],
            DateTimeOffset.UnixEpoch);

        Assert.NotNull(wire);
        Assert.DoesNotContain("project", Encoding.UTF8.GetString(wire));
        Assert.Null(AgentActivityBridgeProtocol.Deserialize(wire)!.WorkspaceLabel);
    }

    [Fact]
    public void The_helpers_own_directory_stands_in_when_the_hook_reports_none()
    {
        var input = Encoding.UTF8.GetBytes("""{ "session_id": "session-sensitive-value" }""");

        var wire = AgentActivityHookPayload.Project(
            input,
            AgentActivityProvider.Codex,
            AgentActivityEvent.PromptSubmitted,
            new byte[32],
            DateTimeOffset.UnixEpoch,
            includeWorkspaceLabel: true,
            currentDirectory: "C:/work/api-gateway/");

        Assert.Equal("api-gateway", AgentActivityBridgeProtocol.Deserialize(wire!)!.WorkspaceLabel);
    }

    [Theory]
    [InlineData("C:/", null)]
    [InlineData(@"C:\work\", "work")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Roots_and_empty_directories_produce_no_label(string? directory, string? expected)
    {
        Assert.Equal(expected, AgentActivityHookPayload.WorkspaceLabelFor(directory));
    }

    [Fact]
    public void An_overlong_folder_name_keeps_its_beginning()
    {
        var label = AgentActivityHookPayload.WorkspaceLabelFor("C:/work/" + new string('a', 80) + "-tail");

        Assert.Equal(AgentActivityBridgeProtocol.MaximumWorkspaceLabelLength, label!.Length);
        Assert.StartsWith("aaaa", label);
        Assert.DoesNotContain("tail", label);
    }
}
