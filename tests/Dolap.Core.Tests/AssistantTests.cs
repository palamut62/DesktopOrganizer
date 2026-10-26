using System.Net;
using System.Text;
using System.Text.Json;
using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class AssistantTests
{
    static AssistantAction A(string kind, string id = "", string target = "", string name = "", string value = "", params string[] ids) => new(kind, id, target, name, value, ids.ToList());

    [Fact]
    public void DesktopInboxIsIdempotentAndCanBeDisabledWithoutDeletingItems()
    {
        using var s = new CollectionStore(":memory:");
        var c = s.EnsureDesktopInbox(@"C:\Desktop")!;
        var item = s.AddShortcut(c.Id, new("NusaSetup.zip", ShortcutKind.File, @"C:\Desktop\NusaSetup.zip"))!;
        Assert.Equal(c.Id, s.EnsureDesktopInbox(@"C:\Desktop")!.Id); Assert.Single(s.GetCategories());
        s.SaveUsability(s.LoadUsability() with { DesktopInbox = false });
        Assert.Null(s.EnsureDesktopInbox(@"C:\Desktop")); Assert.Null(s.GetCategories()[0].LinkedFolder);
        Assert.Equal(item, Assert.Single(s.GetShortcuts()));
        s.SaveUsability(s.LoadUsability() with { DesktopInbox = true });
        Assert.Equal(c.Id, s.EnsureDesktopInbox(@"C:\Desktop")!.Id);
    }

    [Fact]
    public void AssistantPreviewDoesNotMutateAndValidatedPlanIsAtomic()
    {
        using var s = new CollectionStore(":memory:");
        var c = s.AddCategory("Gelenler", "folder", "#123456"); var target = s.AddCategory("Arşiv", "folder", "#123456");
        var item = s.AddShortcut(c.Id, new("NusaSetup.zip", ShortcutKind.File, @"C:\Desktop\NusaSetup.zip"))!;
        var plan = new AssistantPlan("Öneri", [A("move_shortcuts", target: target.Id.ToString(), ids: item.Id.ToString()), A("set_setting", s.FirstCabinetId().ToString(), name: "ThemeId", value: "blocks")]);
        var before = s.AssistantState(); Assert.Equal(2, s.PreviewAssistant(plan).Count); Assert.Equal(before, s.AssistantState());
        s.ApplyAssistant(plan); Assert.Equal(target.Id, s.GetShortcuts()[0].CategoryId); Assert.Equal("blocks", s.LoadSettings().ThemeId);
        var invalid = new AssistantPlan("", [A("remove_shortcuts", ids: item.Id.ToString()), A("set_setting", s.FirstCabinetId().ToString(), name: "Scale", value: "100")]);
        Assert.Throws<DolapException>(() => s.ApplyAssistant(invalid)); Assert.Single(s.GetShortcuts());
    }

    [Theory]
    [InlineData("execute_shell")]
    [InlineData("delete_file")]
    [InlineData("launch_url")]
    public void UnknownCapabilitiesAreRejected(string kind)
    {
        using var s = new CollectionStore(":memory:");
        Assert.Throws<DolapException>(() => s.PreviewAssistant(new("", [A(kind, value: "anything")])));
    }

    [Fact]
    public void ProviderStateExcludesFullFilePathsArgumentsAndLinkedFolderPaths()
    {
        using var s = new CollectionStore(":memory:"); var c = s.AddCategory("Evrak", "folder", "#123456");
        s.SetLinkedFolder(c.Id, @"C:\private-folder");
        s.AddShortcut(c.Id, new("Belge", ShortcutKind.File, @"C:\private-folder\file.txt", Arguments: "secret-argument"));
        var state = s.AssistantState(); Assert.DoesNotContain("private-folder", state); Assert.DoesNotContain("secret-argument", state);
        Assert.Contains("Belge", state);
    }

    sealed class FakeHandler : HttpMessageHandler
    {
        public string Body = ""; public string? Authorization;
        public string Reply = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.Parameter;
            return new(HttpStatusCode.OK) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task ClientUsesRealSchemaAndKeepsKeyOutOfBody()
    {
        var handler = new FakeHandler { Reply = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content = JsonSerializer.Serialize(new AssistantPlan("Hazır", [])) } } }, usage = new { cost = .001m } }) };
        using var http = new HttpClient(handler); var client = new OpenRouterClient(http, () => "test-key-only");
        var result = await client.Plan("test/model", "test", "{}", default);
        Assert.Empty(result.Plan.Actions); Assert.Equal(.001m, result.Cost);
        Assert.Equal("test-key-only", handler.Authorization); Assert.DoesNotContain("test-key-only", handler.Body);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("json_schema", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        handler.Reply = """{"choices":[{"finish_reason":"length","message":{"content":"{}"}}]}""";
        await Assert.ThrowsAsync<DolapException>(() => client.Plan("test/model", "test", "{}", default));
    }

    [Fact]
    public void ModelCapabilitiesAndPricesAreReadFromApiData()
    {
        var models = OpenRouterClient.ParseModels("""{"data":[{"id":"test/model","name":"Test","context_length":32000,"pricing":{"prompt":"0.000001","completion":"0.000002"},"supported_parameters":["structured_outputs"]}]}""");
        Assert.True(models[0].Structured); Assert.Equal(32000, models[0].Context); Assert.Equal(.000001m, models[0].InputPrice);
    }

    [Fact]
    public void AutomaticClassificationOnlyAcceptsCurrentItemsAndNormalDrawers()
    {
        using var s = new CollectionStore(":memory:"); var inbox = s.EnsureDesktopInbox(@"C:\Desktop")!;
        var archive = s.AddCategory("Arşiv", "folder", "#123456");
        var item = s.AddShortcut(inbox.Id, new("NusaSetup.zip", ShortcutKind.File, @"C:\Desktop\NusaSetup.zip"))!;
        var valid = new AssistantPlan("", [A("move_shortcuts", target: archive.Id.ToString(), ids: item.Id.ToString())]);
        Assert.Equal(new DesktopAssignment(item.Id, archive.Id), Assert.Single(DesktopClassification.Validate(valid, [item], s.GetCategories())));
        Assert.Throws<DolapException>(() => DesktopClassification.Validate(new("", [A("remove_shortcuts", ids: item.Id.ToString())]), [item], s.GetCategories()));
        Assert.Throws<DolapException>(() => DesktopClassification.Validate(new("", [A("move_shortcuts", target: inbox.Id.ToString(), ids: item.Id.ToString())]), [item], s.GetCategories()));
        Assert.Throws<DolapException>(() => DesktopClassification.Validate(valid, [], s.GetCategories()));
        Assert.Throws<DolapException>(() => DesktopClassification.Validate(new("", [valid.Actions[0], valid.Actions[0]]), [item], s.GetCategories()));
    }
}
