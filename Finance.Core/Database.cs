using Microsoft.Data.Sqlite;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Finance.Core;
public sealed class BusinessException(string message) : Exception(message);
public static class Value
{
    public static long Money(decimal amount) => checked((long)decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero));
    public static decimal Rupees(long amount) => amount / 100m;
    public static string Display(long amount) => Rupees(amount).ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
    public static int Day(DateOnly date) => date.DayNumber;
    public static string Date(int day) => DateOnly.FromDayNumber(day).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    public static string FY(int day) { var d = DateOnly.FromDayNumber(day); int y = d.Month >= 4 ? d.Year : d.Year - 1; return $"{y}-{(y + 1) % 100:00}"; }
    public static long Round(long paise, string rule) => rule switch { "Rupee" => Money(decimal.Round(Rupees(paise), 0, MidpointRounding.AwayFromZero)), "Ceiling" => Money(decimal.Ceiling(Rupees(paise))), "Paise" => paise, _ => throw new BusinessException("Choose a valid rounding rule.") };
    public static decimal Dec(object? x) => Convert.ToDecimal(x, CultureInfo.InvariantCulture);
    public static long Long(object? x) => x is null or DBNull ? 0 : Convert.ToInt64(x, CultureInfo.InvariantCulture);
    public static string Text(object? x) => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "";
    public static void Require(bool ok, string message) { if (!ok) throw new BusinessException(message); }
    public static string Json(object? value) => JsonSerializer.Serialize(value);
}
public sealed class Db : IDisposable
{
    public SqliteConnection Connection { get; }
    public SqliteTransaction? Transaction { get; private set; }
    public Db(string path, bool readOnly = false)
    {
        Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        Connection.Open(); Exec("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;");
    }
    public void Begin() => Transaction = Connection.BeginTransaction();
    public void Commit() { Transaction!.Commit(); Transaction.Dispose(); Transaction = null; }
    public SqliteCommand Command(string sql, params object?[] args)
    {
        var c = Connection.CreateCommand(); c.CommandText = sql; c.Transaction = Transaction;
        for (int i = 0; i < args.Length; i++) c.Parameters.AddWithValue("@p" + i, args[i] ?? DBNull.Value);
        return c;
    }
    public int Exec(string sql, params object?[] args) { using var c = Command(sql, args); return c.ExecuteNonQuery(); }
    public object? Scalar(string sql, params object?[] args) { using var c = Command(sql, args); return c.ExecuteScalar(); }
    public long N(string sql, params object?[] args) => Value.Long(Scalar(sql, args));
    public DataTable Table(string sql, params object?[] args) { using var c = Command(sql, args); using var r = c.ExecuteReader(); var t = new DataTable(); for(int i=0;i<r.FieldCount;i++) t.Columns.Add(r.GetName(i),r.GetFieldType(i)); while(r.Read()){var values=new object[r.FieldCount];r.GetValues(values);t.Rows.Add(values);} return t; }
    public DataRow Row(string sql, params object?[] args) { var t = Table(sql, args); Value.Require(t.Rows.Count == 1, "The selected record no longer exists."); return t.Rows[0]; }
    public long Insert(string table, IDictionary<string, object?> values)
    {
        Exec($"INSERT INTO {table} ({string.Join(',', values.Keys)}) VALUES ({string.Join(',', values.Keys.Select((_, i) => "@p" + i))})", values.Values.ToArray());
        return N("SELECT last_insert_rowid()");
    }
    public void Dispose() { Transaction?.Dispose(); Connection.Dispose(); }
}
public record Line(string Account, long Amount, long? Party = null, long? Employee = null, long? Bank = null, long? Site = null, long? Instrument = null, long? Category = null);
public partial class Engine
{
    public string Path { get; }
    public long UserId { get; private set; }
    public string UserName { get; private set; } = "";
    DateTime lastActivity;
    public Engine(string path)
    {
        Path = System.IO.Path.GetFullPath(path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var d = Open();
        var existing = d.N("PRAGMA application_id");
        Value.Require(existing is 0 or 1179472689, "This is not a Business Finance database.");
        using var stream = typeof(Engine).Assembly.GetManifestResourceStream("Finance.Core.Schema.sql")!;
        d.Exec(new StreamReader(stream).ReadToEnd());
        foreach (var (code, name, category) in new (string, string, string)[] {
            ("BANK","Bank / Cash","Asset"),("AR","Customer receivables","Asset"),("AP","Supplier payables","Liability"),
            ("STAFF","Staff / site advances","Asset"),("ADV","Employee advances","Asset"),("ELOAN","Employee loans","Asset"),
            ("FD","Fixed deposits","Asset"),("ASSET","Other assets","Asset"),("DEPOSIT","Security deposits","Asset"),
            ("BLOAN","Business loans","Liability"),("SALARY","Salary payable","Liability"),("EPF","EPF payable","Liability"),("ESIC","ESIC payable","Liability"),
            ("DEDUCTION","Other deductions payable","Liability"),("CUSTADV","Customer advances","Liability"),
            ("ICGST","Input CGST","Asset"),("ISGST","Input SGST","Asset"),("IIGST","Input IGST","Asset"),
            ("OCGST","Output CGST","Liability"),("OSGST","Output SGST","Liability"),("OIGST","Output IGST","Liability"),
            ("SALES","Sales","Income"),("INCOME","Other income","Income"),("INTERESTIN","Interest income","Income"),
            ("PURCHASE","Purchases","Expense"),("EXPENSE","Business / site expenses","Expense"),("WAGES","Salary expense","Expense"),
            ("INTEREST","Interest expense","Expense"),("CHARGES","Bank / BG charges","Expense"),("ROUND","Rounding differences","Expense"),
            ("CAPITAL","Capital","Equity"),("OPEN","Opening balance equity","Equity") })
            d.Exec("INSERT OR IGNORE INTO Account VALUES(@p0,@p1,@p2)",code,name,category);
        foreach (string category in new[] { "Fuel", "Travelling", "Office Expense", "Rent", "Electricity", "Repairs", "Professional Fees", "Freight", "Loading/Unloading", "Printing & Stationery", "Telephone/Internet", "Miscellaneous" }) d.Exec("INSERT OR IGNORE INTO Category(Name) VALUES(@p0)",category);
        // User-specified initial settings. These are editable configuration, not a compliance certification.
        d.Exec("INSERT OR IGNORE INTO Statutory VALUES(1,@p0,'50','12',1500000,1,'Basic','0.75',2100000,'Gross','Rupee','Ceiling','Paise')", new DateOnly(2000,1,1).DayNumber);
    }
    public Db Open() => new(Path);
    public bool NeedsSetup { get { using var d = Open(); return d.N("SELECT COUNT(*) FROM AppUser") == 0; } }
    public void Setup(string name, string password)
    {
        Value.Require(NeedsSetup, "An administrator already exists."); Value.Require(!string.IsNullOrWhiteSpace(name), "Enter an administrator name."); CheckPassword(password);
        using var d=Open(); d.Begin(); var salt=RandomNumberGenerator.GetBytes(32);
        d.Exec("INSERT INTO AppUser(Id,Name,Salt,Hash,Iterations) VALUES(1,@p0,@p1,@p2,210000)",name.Trim(),salt,Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32));
        d.Commit(); Login(name,password);
    }
    static void CheckPassword(string p) => Value.Require(p.Length>=12,"Use a password of at least 12 characters.");
    public void Login(string name,string password)
    {
        using var d=Open(); var t=d.Table("SELECT * FROM AppUser WHERE Name=@p0",name.Trim());
        Value.Require(t.Rows.Count==1,"The username or password is incorrect."); var r=t.Rows[0];
        Value.Require(Value.Long(r["LockedUntil"])<=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),"Too many failed attempts. Try again in 15 minutes.");
        var hash=Rfc2898DeriveBytes.Pbkdf2(password,(byte[])r["Salt"],(int)Value.Long(r["Iterations"]),HashAlgorithmName.SHA256,32);
        if(!CryptographicOperations.FixedTimeEquals(hash,(byte[])r["Hash"])) {
            d.Exec("UPDATE AppUser SET Failed=Failed+1,LockedUntil=CASE WHEN Failed>=4 THEN @p0 ELSE LockedUntil END WHERE Id=@p1",DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(),r["Id"]);
            throw new BusinessException("The username or password is incorrect.");
        }
        UserId=Value.Long(r["Id"]); UserName=Value.Text(r["Name"]); lastActivity=DateTime.UtcNow;
        d.Exec("UPDATE AppUser SET Failed=0,LockedUntil=0 WHERE Id=@p0",UserId); Audit(d,"User",UserId,"Login");
    }
    public void Logout(){UserId=0;UserName="";}
    public void Authenticate(){Value.Require(UserId>0 && DateTime.UtcNow-lastActivity<TimeSpan.FromMinutes(20),"Your session has expired. Close and reopen the application to sign in.");lastActivity=DateTime.UtcNow;}
    public void ChangePassword(string oldPassword,string newPassword){Authenticate();Login(UserName,oldPassword);CheckPassword(newPassword);using var d=Open();var salt=RandomNumberGenerator.GetBytes(32);d.Exec("UPDATE AppUser SET Salt=@p0,Hash=@p1,Iterations=210000 WHERE Id=@p2",salt,Rfc2898DeriveBytes.Pbkdf2(newPassword,salt,210000,HashAlgorithmName.SHA256,32),UserId);Audit(d,"User",UserId,"Password changed");}
    public T Write<T>(Func<Db,T> action)
    {
        Authenticate(); using var d=Open();d.Begin();
        try {var result=action(d); ValidateControlledBalances(d); d.Commit();return result;}
        catch(SqliteException ex) when(ex.SqliteErrorCode==19){throw new BusinessException("The record conflicts with an existing record or a required linked record is missing. Check duplicate numbers and selected records.");}
    }
    static void ValidateControlledBalances(Db d)
    {
        // Validate running balances, including backdated entries against later recoveries.
        long invalid=d.N(@"WITH Daily AS (
            SELECT p.Account,v.Day,
            CASE WHEN p.Account IN ('STAFF','ADV','ELOAN','SALARY') THEN p.EmployeeId WHEN p.Account='CUSTADV' THEN p.PartyId ELSE 0 END AS Emp,
            CASE WHEN p.Account='STAFF' THEN p.SiteId ELSE 0 END AS Site,
            CASE WHEN p.Account IN ('ELOAN','FD','BLOAN') THEN p.InstrumentId ELSE 0 END AS Instrument,
            SUM(p.Amount) AS Amount
            FROM Posting p JOIN Voucher v ON v.Id=p.VoucherId
            WHERE p.Account IN ('STAFF','ADV','ELOAN','FD','BLOAN','SALARY','EPF','ESIC','DEDUCTION','CUSTADV')
            GROUP BY p.Account,v.Day,Emp,Site,Instrument)
            SELECT COUNT(*) FROM (SELECT Account,
            SUM(Amount) OVER(PARTITION BY Account,Emp,Site,Instrument ORDER BY Day ROWS UNBOUNDED PRECEDING) AS Running FROM Daily)
            WHERE (Account IN ('STAFF','ADV','ELOAN','FD') AND Running<0)
               OR (Account IN ('BLOAN','SALARY','EPF','ESIC','DEDUCTION','CUSTADV') AND Running>0)");
        Value.Require(invalid==0,"This entry would overdraw an advance/deposit or overpay a liability, including later dated transactions. Review related recoveries/payments first.");
    }
    internal void Audit(Db d,string entity,long? id,string action,object? old=null,object? value=null,string reason="") => d.Exec("INSERT INTO Audit(UserId,TimeUtc,Entity,EntityId,Action,OldValue,NewValue,Reason) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)",UserId==0?null:UserId,DateTime.UtcNow.ToString("O"),entity,id,action,old is null?null:Value.Json(old),value is null?null:Value.Json(value),reason);
    internal void Unlocked(Db d,int day){Value.Require(day>=new DateOnly(1900,1,1).DayNumber && day<=new DateOnly(2200,12,31).DayNumber,"Date must be between 01-01-1900 and 31-12-2200.");Value.Require(d.N("SELECT COUNT(*) FROM PeriodLock WHERE Locked=1 AND @p0 BETWEEN FromDay AND ToDay",day)==0,"This accounting period is locked.");}
    internal long Post(Db d,string kind,int day,string narration,IEnumerable<Line> entries,string? token=null,long? reversal=null)
    {
        Unlocked(d,day); var lines=entries.Where(x=>x.Amount!=0).ToArray();
        Value.Require(lines.Length>=2 && lines.Sum(x=>(decimal)x.Amount)==0,"Posting rejected: debits and credits must balance.");
        Value.Require(lines.All(x=>Math.Abs((decimal)x.Amount)<=900000000000000),"The amount exceeds the supported transaction limit.");
        foreach(var l in lines){
            if(l.Account=="BANK") Value.Require(l.Bank>0,"Select a bank or cash account.");
            if(l.Account is "AR" or "AP" or "CUSTADV") Value.Require(l.Party>0,"Select a customer or supplier.");
            if(l.Account is "STAFF" or "ADV" or "ELOAN" or "SALARY") Value.Require(l.Employee>0,"Select an employee.");
            if(l.Account is "FD" or "ELOAN" or "BLOAN") Value.Require(l.Instrument>0,"Select the related deposit or loan.");
        }
        var fy=Value.FY(day); d.Exec("INSERT OR IGNORE INTO Sequence VALUES(@p0,@p1,1)",fy,kind);
        var next=d.N("SELECT Next FROM Sequence WHERE FY=@p0 AND Kind=@p1",fy,kind); d.Exec("UPDATE Sequence SET Next=Next+1 WHERE FY=@p0 AND Kind=@p1",fy,kind);
        string prefix=Value.Text(d.Scalar("SELECT Value FROM Setting WHERE Key=@p0","Prefix."+kind));if(prefix=="")prefix=kind.Replace(" ","").ToUpperInvariant();
        var id=d.Insert("Voucher",new Dictionary<string,object?>{{"Token",token??Guid.NewGuid().ToString()},{"Kind",kind},{"Number",$"{prefix}/{fy}/{next:00000}"},{"Day",day},{"FY",fy},{"Narration",narration},{"UserId",UserId},{"CreatedUtc",DateTime.UtcNow.ToString("O")},{"ReversalOf",reversal}});
        foreach(var l in lines)d.Exec("INSERT INTO Posting(VoucherId,Account,Amount,PartyId,EmployeeId,BankId,SiteId,InstrumentId,CategoryId) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",id,l.Account,l.Amount,l.Party,l.Employee,l.Bank,l.Site,l.Instrument,l.Category);
        Audit(d,"Voucher",id,"Posted",null,new{kind,day,lines});return id;
    }
    internal long Balance(Db d,string account,long? party=null,long? employee=null,long? bank=null,long? site=null,long? instrument=null,int? through=null) => d.N("SELECT COALESCE(SUM(p.Amount),0) FROM Posting p JOIN Voucher v ON v.Id=p.VoucherId WHERE p.Account=@p0 AND (@p1 IS NULL OR p.PartyId=@p1) AND (@p2 IS NULL OR p.EmployeeId=@p2) AND (@p3 IS NULL OR p.BankId=@p3) AND (@p4 IS NULL OR p.SiteId=@p4) AND (@p5 IS NULL OR p.InstrumentId=@p5) AND (@p6 IS NULL OR v.Day<=@p6)",account,party,employee,bank,site,instrument,through);
    public long Balance(string account,long? party=null,long? employee=null,long? bank=null,long? site=null,long? instrument=null,int? through=null){Authenticate();using var d=Open();return Balance(d,account,party,employee,bank,site,instrument,through);}
    public void Lock(int from,int to,bool locked,string reason) => Write(d=>{Value.Require(to>=from&&!string.IsNullOrWhiteSpace(reason),"Enter valid dates and a reason.");if(locked)d.Exec("INSERT INTO PeriodLock(FromDay,ToDay,Locked,Reason) VALUES(@p0,@p1,1,@p2)",from,to,reason);else d.Exec("UPDATE PeriodLock SET Locked=0 WHERE FromDay=@p0 AND ToDay=@p1",from,to);Audit(d,"Period",null,locked?"Lock":"Unlock",null,new{from,to},reason);return 0;});
    public long Opening(int day,IEnumerable<Line> balances,string reason)=>Write(d=>{Value.Require(!string.IsNullOrWhiteSpace(reason),"Enter the opening balance reference.");var l=balances.ToList();Value.Require(l.All(x=>x.Account!="OPEN"),"Opening equity is calculated automatically.");l.Add(new Line("OPEN",-l.Sum(x=>x.Amount)));return Post(d,"Opening",day,reason,l);});
}
