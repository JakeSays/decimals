// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace DpdTableGenerator;

public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("usage: dpd-tables <output-file>");
            Console.WriteLine();
            Console.WriteLine("  Writes the densely-packed-decimal declet tables as byte spans, built");
            Console.WriteLine("  from the bit rules rather than transcribed.");
            return 2;
        }

        var writer = new DpdTableWriter();
        writer.Write(args[0]);
        Console.WriteLine($"wrote {args[0]}");
        return 0;
    }
}
