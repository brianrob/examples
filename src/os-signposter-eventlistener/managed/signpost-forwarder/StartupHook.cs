using signpost_forwarder;

internal class StartupHook
{
    private static Forwarder Instance;
    public static void Initialize()
    {
        System.Console.WriteLine("Hello!");
        Instance = new Forwarder();
    }
}