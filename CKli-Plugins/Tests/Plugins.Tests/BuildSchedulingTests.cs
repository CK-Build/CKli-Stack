using CK.Monitoring;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The order in which the builds of a roadmap start (<c>BuildPlugin.RoadmapExecutor</c>).
/// <para>
/// The builds that are ready compete for the "--max-dop" monitors: the pivots and their upstreams are served
/// first, then the pivots' downstreams, then the others. Without pivots, the arrival order - the roadmap's
/// topological order - decides.
/// </para>
/// </summary>
public class BuildSchedulingTests
{
    /// <summary>
    /// X-Other comes first in the roadmap (same rank, lower index) and is unrelated to X-Pivot, so a "*build"
    /// from X-Pivot builds both. With a single monitor, the pivot is served first. From the World root there is
    /// no pivot and the roadmap order is kept.
    /// <para>
    /// The "n°k" of the log counts the builds as they start: it is not the roadmap's BuildNumber (X-Pivot's is 2).
    /// </para>
    /// </summary>
    [TestCase( true )]
    [TestCase( false )]
    public async Task the_pivot_is_built_first_Async( bool fromPivot )
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        var rOther = await world.CreateRepoAsync( "X-Other", "v1.0.0" ).ConfigureAwait( false );
        var rPivot = await world.CreateRepoAsync( "X-Pivot", "v1.0.0" ).ConfigureAwait( false );
        TestHelper.TouchAndCommit( rOther.WorkingFolderPath, branchName: null );
        TestHelper.TouchAndCommit( rPivot.WorkingFolderPath, branchName: null );

        // A GrandOutput memory collector, not TestHelper.Monitor.CollectTexts: see PartialBuildFailureTests.
        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            var path = fromPivot ? rPivot.Root : world.WorldRoot;
            (await CKliCommands.ExecAsync( TestHelper.Monitor, path, "*build", "--max-dop", "1" )).ShouldBeTrue();
            var starts = logs.ExtractCurrentTexts().Where( t => t.StartsWith( "Building roadmap n°" ) ).ToArray();
            var (first, second) = fromPivot ? ("X-Pivot", "X-Other") : ("X-Other", "X-Pivot");
            starts.ShouldBe( [$"Building roadmap n°1/2: '{first}'.", $"Building roadmap n°2/2: '{second}'."] );
        }
    }
}
