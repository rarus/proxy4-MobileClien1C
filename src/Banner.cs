using System;

static class Banner
{
    static readonly string[] Art =
    {
        @" ███  ████  ████       ████  ████   ███  █   █ █   █    █  █     █   █  ███  ████       █    ███",
        @"█   █ █   █ █   █      █   █ █   █ █   █  █ █   █ █     █  █     ██ ██ █   █ █   █     ██   █   ",
        @"█████ █   █ ████  ████ ████  ████  █   █   █     █      █████    █ █ █ █   █ ████       █   █   ",
        @"█   █ █   █ █   █      █     █  █  █   █  █ █    █         █     █   █ █   █ █   █      █   █   ",
        @"█   █ ████  ████       █     █   █  ███  █   █   █         █     █   █  ███  ████      ███   ███"
    };

    const string License = "Исключительная лицензия на использование: ООО \"1С-Рарус\", ИНН 7707079463";

    public static void Print()
    {
        foreach (string row in Art)
            Console.WriteLine(row);
        Console.WriteLine(License);
        Console.WriteLine();
    }
}
