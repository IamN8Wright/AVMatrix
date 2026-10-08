try
{
    InNasc.SmokeTests.QualityRegression.Run();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
