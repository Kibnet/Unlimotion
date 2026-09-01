using System.Linq;
using System.Threading.Tasks;
using Unlimotion.Test.StormBdd;

namespace Unlimotion.Test;

[NotInParallel("ServerStorageLiveIntegration")]
[Property("CiMeasurementPackage", "server")]
public class StormServerStorageCrudRealtimeExecutableSpecTests
{
    [Test]
    public async Task ServerStorageCrudRealtimeScenario_ExecutesFeatureSteps()
    {
        var scenario = StormFeatureParser.ParseScenario(
            "features/storm/st-0011-server-storage.feature",
            "SC-0011-002");

        await Assert.That(scenario.Title).IsEqualTo(
            "CRUD операций задач выполняется через аутентифицированные ServiceStack endpoints, а SignalR-подключение может доставлять обновления между клиентами.");
        await Assert.That(scenario.Tags).Contains("@scenario:SC-0011-002");
        await Assert.That(scenario.Tags).Contains("@test:TS-0017");
        await Assert.That(scenario.Tags).Contains("@test:TS-0018");
        await Assert.That(scenario.Tags).Contains("@test:TS-0019");
        await Assert.That(scenario.Tags).Contains("@test:TS-0020");
        await Assert.That(scenario.Steps.Count).IsEqualTo(4);

        var runner = new StormScenarioRunner(ServerStorageAuthStepDefinitions.Create());
        var context = await runner.ExecuteAsync(scenario);

        var expectedStepDefinitionIds = new[] { "SD-0022", "SD-0023", "SD-0024", "SD-0026" };
        await Assert.That(context.ExecutedStepDefinitionIds.Count).IsEqualTo(expectedStepDefinitionIds.Length);
        foreach (var id in expectedStepDefinitionIds)
        {
            await Assert.That(context.ExecutedStepDefinitionIds).Contains(id);
        }

        await Assert.That(scenario.Steps.Select(step => step.Keyword).ToArray())
            .IsEquivalentTo(["Дано", "И", "Когда", "Тогда"]);
    }
}
