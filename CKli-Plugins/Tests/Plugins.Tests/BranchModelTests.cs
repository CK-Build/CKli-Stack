using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

public class BranchModelTests
{
    [Test]
    public async Task work_on_CKt_init_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes("CKt(init)");
        var context = remotes.Clone(clonedFolder);
        var display = (StringScreen)context.Screen;

        display.Clear();
        (await CKliCommands.ExecAsync(TestHelper.Monitor, context, "issue")).ShouldBeTrue();
        display.ToString().ShouldBe("""
            > CKt-Core (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'master'.
            > CKt-ActivityMonitor (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'master'.
            > CKt-PerfectEvent (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'master'.
            > CKt-Monitoring (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'master'.
            ❰✓❱

            """);

        display.Clear();
        (await CKliCommands.ExecAsync(TestHelper.Monitor, context, "issue", "--fix")).ShouldBeTrue();
        display.ToString().ShouldBe("""
            ❰✓❱

            """);
    }

}
