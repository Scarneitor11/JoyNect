namespace JoyNect.Core.Cli;

/// <summary>Parsed command line options.</summary>
public sealed class CliOptions
{
    public bool Json { get; private set; }

    public bool Verbose { get; private set; }

    public bool TestPad { get; private set; }

    /// <summary>enumerate: list every HID interface instead of only Nintendo's.</summary>
    public bool AllHidDevices { get; private set; }

    public string? OutputPath { get; private set; }

    /// <summary>Free-form trailing arguments, used by commands such as replay.</summary>
    public List<string> Positional { get; } = [];

    /// <summary>
    /// Parses options leniently: an unknown flag is ignored rather than fatal, because the WPF app
    /// also receives Windows' own command line switches such as <c>-Embedding</c>.
    /// </summary>
    public static CliOptions Parse(IEnumerable<string> args)
    {
        var options = new CliOptions();
        string? pending = null;

        foreach (var raw in args)
        {
            var arg = raw.Trim();
            if (pending is not null)
            {
                if (string.Equals(pending, "--out", StringComparison.OrdinalIgnoreCase))
                {
                    options.OutputPath = arg;
                }

                pending = null;
                continue;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--json":
                    options.Json = true;
                    break;
                case "--verbose":
                case "-v":
                    options.Verbose = true;
                    break;
                case "--test-pad":
                    options.TestPad = true;
                    break;
                case "--all":
                    options.AllHidDevices = true;
                    break;
                case "--out":
                    pending = "--out";
                    break;
                default:
                    if (!arg.StartsWith('-'))
                    {
                        options.Positional.Add(arg);
                    }

                    break;
            }
        }

        return options;
    }
}
