namespace DocDogfood;

public static class BrokenProbe
{
    public static int Add(int a, int b) => a + b;

    public static void Main()
    {
        var sum = Add(2, 3);
        System.Console.WriteLine(sum);
    }
}
