using Finance.Core;
namespace Finance.Desktop;
internal static class SmokeChecks
{
    // Explicit CI-only entry point. It never opens the normal user's database.
    public static int Run(string reportPath)
    {
        string directory = Path.Combine(Path.GetTempPath(), "BusinessFinance-Smoke-" + Guid.NewGuid().ToString("N"));
        var results = new List<string>();
        Directory.CreateDirectory(directory);
        try
        {
            var engine = new Engine(Path.Combine(directory, "smoke.db"));
            using (var login = new Login(engine))
            {
                login.CreateControl();
                results.Add("PASS | Login/setup form constructed");
            }
            engine.Setup("ci-smoke-admin", Guid.NewGuid().ToString("N"));
            Exception? failure = null;
            using (var main = new MainForm(engine))
            {
                main.Shown += (_, _) =>
                {
                    try { main.VerifyNavigationForSmoke(results); }
                    catch (Exception ex) { failure = ex; }
                    finally { main.Close(); }
                };
                Application.Run(main);
            }
            if (failure != null) throw failure;
            results.Add("PASS | " + engine.Integrity());
            results.Add("PASS | Native SQLite and desktop dependencies loaded from the portable application");
            results.Add("LIMIT | This is a navigation/launch smoke check, not complete interactive or print acceptance.");
            File.WriteAllLines(reportPath, results);
            return 0;
        }
        catch (Exception ex)
        {
            results.Add("FAIL | " + ex);
            File.WriteAllLines(reportPath, results);
            return 1;
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
        }
    }
}
