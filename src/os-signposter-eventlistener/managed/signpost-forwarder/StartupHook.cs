using signpost_forwarder;

internal class StartupHook
{
    private static Forwarder Instance;
    public static void Initialize()
    {
        Instance = new Forwarder();
    }
}