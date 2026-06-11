using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ScannerCore;
using Spectre.Console;

namespace ScannerConsole
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var target = args.Length > 0
                ? args[0]
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!Directory.Exists(target))
            {
                AnsiConsole.MarkupLine($"[red]Directory does not exist:[/] {Markup.Escape(target)}");
                return 1;
            }

            var scanner = new DriveScanner();
            FsItem root = null;
            Exception failure = null;
            var elapsed = Stopwatch.StartNew();

            var worker = new Thread(() =>
            {
                try
                {
                    root = scanner.ScanDirectory(target, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            AnsiConsole.MarkupLine($"[bold]Scanning:[/] {Markup.Escape(target)}");
            worker.Start();

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Scanning directory tree...", ctx =>
                {
                    while (worker.IsAlive)
                    {
                        var current = scanner.CurrentScanned ?? target;
                        ctx.Status($"Scanning {Markup.Escape(current)}");
                        Thread.Sleep(250);
                    }
                });

            worker.Join();
            elapsed.Stop();

            if (failure != null)
            {
                AnsiConsole.WriteException(failure);
                return 1;
            }

            if (root == null)
            {
                AnsiConsole.MarkupLine("[red]Scan did not return a root item.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Complete[/] in {elapsed.Elapsed.TotalSeconds:F2} seconds");
            AnsiConsole.MarkupLine($"[bold]Total size:[/] {Markup.Escape(Humanize.Size(root.Size))}");

            var table = new Table { Title = new TableTitle("Inaccessible Paths") };
            table.AddColumn("Path");
            foreach (var path in scanner.Inaccessible)
            {
                table.AddRow(Markup.Escape(path));
            }
            AnsiConsole.Write(table);

            return 0;
        }
    }
}
