using System.Runtime.CompilerServices;
using Drydock.Core;

namespace Drydock.Cli;

static class Program
{
    const string Usage = """
        usage: drydock [--game-dir <Game2 folder>] <command>

        commands:
          items [query]                                  list item definitions, optionally filtered
          inventories <save> [--owner <text>] [--all]    list inventories by number; --all includes empty ones
          set <save> <#> <item> <amount> [--dry-run]     set an item's amount, 0 removes it
          add <save> <#> <item> <amount> [--dry-run]     add an item, topping up an existing stack
          remove <save> <#> <item> [--dry-run]           remove every stack of an item

        debugging:
          paths <file.vrb> <field>                       print the type path to every field with that name
          show <file.vrb> <name> [count]                 print fields whose name contains <name>
          bytes <save> <owner>                           hex dump of the item lists of matching inventories

        <save> is a save folder path or a folder name in SaveGames. <#> is the number from 'inventories'.
        <item> is a name, localization key or GUID from 'items'. Close the game before writing.
        """;

    static int Main(string[] args)
    {
        var argList = args.ToList();
        string? gameDir = TakeOption(argList, "--game-dir");
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
        switch (args.FirstOrDefault())
        {
            case "items" when args.Count <= 2:
                Commands.Items(args.ElementAtOrDefault(1));
                return 0;
            case "inventories":
            {
                var owner = TakeOption(args, "--owner");
                bool all = args.Remove("--all");
                if (args.Count != 2)
                    break;
                Commands.Inventories(args[1], owner, all);
                return 0;
            }
            case "set" when args.Count == 5:
                return Commands.Edit(Commands.EditKind.Set, args[1], args[2], args[3], args[4], dryRun);
            case "add" when args.Count == 5:
                return Commands.Edit(Commands.EditKind.Add, args[1], args[2], args[3], args[4], dryRun);
            case "remove" when args.Count == 4:
                return Commands.Edit(Commands.EditKind.Remove, args[1], args[2], args[3], null, dryRun);
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
