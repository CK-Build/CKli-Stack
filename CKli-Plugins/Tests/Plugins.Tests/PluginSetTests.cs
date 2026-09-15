using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

/// <summary>
/// "ckli plugin set" and "ckli plugin unset" configure an attribute of a &lt;Plugins&gt; child element of the
/// World definition file without editing the Xml by hand.
/// <para>
/// The dispatch is "manual" on both sides: the attributes are described by the message a plugin publishes on
/// the <see cref="WorldEvents.PluginInfo"/> event ("ckli plugin info") and they are written by its
/// <c>OnPluginSetAsync</c> override. The identifier is submitted to each primary plugin until one answers a
/// non null result, so the "Plugin.Attribute" long form is required only to disambiguate a name that more
/// than one plugin supports.
/// </para>
/// </summary>
public class PluginSetTests
{
    /// <summary>
    /// The short form finds the only plugin that supports the attribute. Unsetting removes the attribute
    /// altogether: the plugin's own default applies again, it is not written back as an explicit value.
    /// </summary>
    [Test]
    public async Task set_then_unset_a_boolean_attribute_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        PluginAttribute( stack, "VersionTag", "RemoveUselessFakeTag" ).ShouldBeNull();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "RemoveUselessFakeTag", "true" )).ShouldBeTrue();
        PluginAttribute( stack, "VersionTag", "RemoveUselessFakeTag" ).ShouldBe( "true" );

        // Setting "false" writes the attribute: this is not the same as unsetting it (even though the
        // VersionTag default happens to be false).
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "RemoveUselessFakeTag", "false" )).ShouldBeTrue();
        PluginAttribute( stack, "VersionTag", "RemoveUselessFakeTag" ).ShouldBe( "false" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "unset", "RemoveUselessFakeTag" )).ShouldBeTrue();
        PluginAttribute( stack, "VersionTag", "RemoveUselessFakeTag" ).ShouldBeNull();
    }

    /// <summary>
    /// The 4 attributes of the 3 plugins that support them, each set through its "Plugin.Attribute" long form.
    /// The long form is honored: an attribute is submitted to the named plugin only.
    /// </summary>
    [Test]
    public async Task the_long_form_targets_one_plugin_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "BranchModel.AutoFixUselessBranch", "false" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "VersionTag.AutoFixRemovableTag", "true" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "VersionTag.RemoveUselessFakeTag", "true" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "Publish.KeepLocalReleaseAfterPublish", "true" )).ShouldBeTrue();

        PluginAttribute( stack, "BranchModel", "AutoFixUselessBranch" ).ShouldBe( "false" );
        PluginAttribute( stack, "VersionTag", "AutoFixRemovableTag" ).ShouldBe( "true" );
        PluginAttribute( stack, "VersionTag", "RemoveUselessFakeTag" ).ShouldBe( "true" );
        PluginAttribute( stack, "Publish", "KeepLocalReleaseAfterPublish" ).ShouldBe( "true" );

        // The plugin short name is case insensitive (the attribute name is too).
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "unset", "publish.keeplocalreleaseafterpublish" )).ShouldBeTrue();
        PluginAttribute( stack, "Publish", "KeepLocalReleaseAfterPublish" ).ShouldBeNull();
    }

    /// <summary>
    /// A boolean attribute takes the Xml "true" or "false" and nothing else: no "1", no "True", no "yes".
    /// The command fails and the definition file is left untouched.
    /// </summary>
    [TestCase( "True" )]
    [TestCase( "1" )]
    [TestCase( "yes" )]
    public async Task an_invalid_boolean_value_is_an_error_Async( string value )
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "RemoveUselessFakeTag", value )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( $"Invalid value '{value}' for the boolean attribute 'RemoveUselessFakeTag' of the 'VersionTag' plugin." ) );
        }
        PluginAttribute( stack, "VersionTag", "RemoveUselessFakeTag" ).ShouldBeNull();
    }

    /// <summary>
    /// An attribute that no plugin supports and a plugin that doesn't exist are two different errors: both
    /// send the user to "ckli plugin info", which is where the supported attributes are described.
    /// </summary>
    [Test]
    public async Task unknown_attribute_and_unknown_plugin_are_errors_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "NoSuchThing", "true" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Attribute 'NoSuchThing' is not supported by any plugin of this World." ) );

            // The attribute exists but not on the named plugin: the submission is restricted to it.
            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "BranchModel.RemoveUselessFakeTag", "true" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Attribute 'RemoveUselessFakeTag' is not supported by the 'BranchModel' plugin." ) );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "unset", "NoSuch.Thing" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Plugin 'NoSuch' not found: it is not an enabled primary plugin of this World." ) );
        }
        PluginAttribute( stack, "BranchModel", "RemoveUselessFakeTag" ).ShouldBeNull();
    }

    /// <summary>
    /// The configuration that "ckli plugin set" writes is the one the plugins read on the next run: the
    /// message published by "ckli plugin info" follows the new value.
    /// </summary>
    [Test]
    public async Task the_new_value_is_read_back_by_the_plugin_Async()
    {
        using var testEnv = await TestHelper.CKliCreateFakeBuildTestEnvAsync().ConfigureAwait( false );
        var stack = await testEnv.CreateStackAsync( pluginConfigurationEditor: Helper.ConfigureFakeFeeds ).ConfigureAwait( false );
        var world = stack.DefaultWorld;
        var display = stack.Screen;

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "info", "--skip-pull-stack" )).ShouldBeTrue();
        display.ToString().ShouldContain( "+fake version tags that are published are removed by" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "set", "RemoveUselessFakeTag", "true" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, world.WorldRoot, "plugin", "info", "--skip-pull-stack" )).ShouldBeTrue();
        display.ToString().ShouldContain( "+fake version tags that are published are automatically deleted." );

        // A plugin that supports more than one attribute describes each of them: the messages are stacked
        // rather than replacing each other (VersionTag calls AddMessage twice).
        display.ToString().ShouldContain( "AutoFixRemovableTag" );
    }

    static string? PluginAttribute( FakeBuildStack stack, string pluginName, string attributeName )
    {
        var root = XDocument.Load( stack.StackRoot.AppendPart( StackRepository.PublicStackName ).AppendPart( "Test.xml" ) ).Root!;
        return root.Element( "Plugins" )?.Element( pluginName )?.Attribute( attributeName )?.Value;
    }
}
