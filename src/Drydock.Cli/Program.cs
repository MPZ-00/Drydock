using System.Globalization;
using System.Runtime.CompilerServices;
using Drydock.Core;

namespace Drydock.Cli;

static class Program
{
    const string Usage = """
        usage: drydock [--game-dir <Game2 folder>] <command>

        commands:
          saves                                            list saves, newest first
          items [query]                                    list item definitions, optionally filtered
          inventories <save> [<target>] [--all]            list your inventories, or show what a target holds
          set <save> <target> <item> <amount> [--dry-run]  make the inventory hold exactly this amount, 0 removes it
                                                           --force overrides slot and capacity limits
          add <save> <target> <item> <amount> [--dry-run]  add an item, topping up an existing stack
          remove <save> <target> <item> [--dry-run]        remove every stack of an item

        debugging:
          paths <file.vrb> <field>                         print the type path to every field with that name
          show <file.vrb> <name> [count]                   print fields whose name contains <name>
          bytes <save> <owner>                             hex dump of the item lists of matching inventories

        <save>    number from 'saves', part of the save's name, or a folder path
        <target>  player, player:consumables, player:datapads, "Aquila/Cargo Container", or #number from 'inventories'
        <item>    name, localization key or GUID from 'items'
        Close the game before writing.
        """;

    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var argList = args.ToList();
        string? gameDir = TakeOption(argList, "--game-dir");

        // before loading anything, parsing while the game runs is slow
        bool writes = argList.FirstOrDefault() is "set" or "add" or "remove" && !argList.Contains("--dry-run");
        if (writes && SaveWriter.GameRunning())
        {
            Console.Error.WriteLine($"{SaveWriter.GameRunningReason} Quit it completely, then run this again, or add --dry-run.");
            return 1;
        }

        try
        {
            GameInstall.Initialize(gameDir);
            return Run(argList);
        }
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    // separate method so no game type is touched before the assembly resolver is registered
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(List<string> args)
    {
        bool dryRun = args.Remove("--dry-run");
        bool force = args.Remove("--force");
        switch (args.FirstOrDefault())
        {
            case "saves" when args.Count == 1:
                Commands.Saves();
                return 0;
            case "items" when args.Count <= 2:
                Commands.Items(args.ElementAtOrDefault(1));
                return 0;
            case "inventories":
            {
                bool all = args.Remove("--all");
                if (args.Count is not (2 or 3))
                    break;
                Commands.Inventories(args[1], args.ElementAtOrDefault(2), all);
                return 0;
            }
            case "set" when args.Count == 5:
                return Commands.Edit(Commands.EditKind.Set, args[1], args[2], args[3], args[4], dryRun, force);
            case "add" when args.Count == 5:
                return Commands.Edit(Commands.EditKind.Add, args[1], args[2], args[3], args[4], dryRun, force);
            case "remove" when args.Count == 4:
                return Commands.Edit(Commands.EditKind.Remove, args[1], args[2], args[3], null, dryRun, force);
            case "bytes" when args.Count == 3:
                Commands.Bytes(args[1], args[2]);
                return 0;
            case "show" when args.Count is 3 or 4:
                Commands.Show(args[1], args[2], args.Count == 4 ? int.Parse(args[3]) : 1);
                return 0;
            case "paths" when args.Count == 3:
                Commands.Paths(args[1], args[2]);
                return 0;
        }
        Console.Error.WriteLine(Usage);
        return 2;
    }

    static string? TakeOption(List<string> args, string name)
    {
        int i = args.IndexOf(name);
        if (i < 0 || i + 1 >= args.Count)
            return null;
        var value = args[i + 1];
        args.RemoveRange(i, 2);
        return value;
    }
}
