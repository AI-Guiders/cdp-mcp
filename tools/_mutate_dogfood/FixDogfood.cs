namespace MutateDogfood;

public static class FixDogfood
{
    public static string Ping()
    {
        return "ok";
    }

    /// <summary>Sniper dogfood: From while / Till body.</summary>
    public static int Run(int n)
    {
        var total = 0;
        while (n > 0)
        {
            if (n % 2 == 0)
                total += n;
            n--;
        }

        return total;
    }
}
