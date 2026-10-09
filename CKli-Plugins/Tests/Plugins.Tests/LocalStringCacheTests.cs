using CK.Core;
using CKli;
using CKli.Build.Plugin;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// The <see cref="LocalStringCache"/> (the "TestRun.Sha" cache) is shared by the parallel builds.
/// </summary>
public class LocalStringCacheTests
{
    [Test]
    public async Task concurrent_adds_are_all_saved_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var fakeStack = await testEnv.CreateStackAsync().ConfigureAwait( false );
        using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, fakeStack.DefaultWorld.WorldRoot, out _, skipPullStack: true ).ShouldNotBeNull();

        var cache = new LocalStringCache( stack.DefaultWorldName, "Concurrency" );
        var warnings = new ConcurrentBag<string>();
        Parallel.For( 0, 200, i =>
        {
            // An activity monitor is not thread safe: one per concurrent "build".
            var m = new ActivityMonitor();
            IReadOnlyList<ActivityMonitorSimpleCollector.Entry> entries;
            using( m.CollectEntries( out entries, LogLevelFilter.Warn ) )
            {
                cache.Add( m, $"K{i}" );
                cache.Contains( m, $"K{i}" ).ShouldBeTrue();
            }
            foreach( var e in entries ) warnings.Add( e.Text );
        } );
        warnings.ShouldBeEmpty();

        var saved = File.ReadAllLines( LocalStringCache.GetFilePath( stack.DefaultWorldName, "Concurrency" ) );
        saved.Order().ShouldBe( Enumerable.Range( 0, 200 ).Select( i => $"K{i}" ).Order() );
    }
}
