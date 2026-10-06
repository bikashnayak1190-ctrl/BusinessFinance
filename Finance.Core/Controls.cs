using System.Data;
using System.Security.Cryptography;
namespace Finance.Core;
public partial class Engine
{
    public void CancelInvoice(long id,int day,string reason)=>Write(d=>{var r=d.Row("SELECT * FROM Invoice WHERE Id=@p0",id);Value.Require(Value.Long(r["Cancelled"])==0,"Invoice is already cancelled.");Value.Require(!string.IsNullOrWhiteSpace(reason),"A cancellation reason is required.");Unlocked(d,(int)Value.Long(r["Day"]));if(r["VoucherId"] is not DBNull)Reverse(d,Value.Long(r["VoucherId"]),day,reason);d.Exec("UPDATE Invoice SET Cancelled=1 WHERE Id=@p0",id);Audit(d,"Invoice",id,"Cancelled",null,null,reason);return 0;});
    public long Reverse(long id,int day,string reason)=>Write(d=>Reverse(d,id,day,reason));
    long Reverse(Db d,long id,int day,string reason,bool payroll=false)
    {
        Value.Require(!string.IsNullOrWhiteSpace(reason),"A reversal reason is required.");var v=d.Row("SELECT * FROM Voucher WHERE Id=@p0",id);
        Value.Require(v["ReversedBy"] is DBNull && v["ReversalOf"] is DBNull,"This voucher has already been reversed or is a reversal.");
        Unlocked(d,(int)Value.Long(v["Day"]));Value.Require(day>=Value.Long(v["Day"]),"Reversal date cannot precede the original transaction.");
        string kind=Value.Text(v["Kind"]);Value.Require(kind!="Payroll"||payroll,"Use Reopen Payroll to reverse payroll.");Value.Require(kind!="FD Closure","FD closure reversal requires a reviewed correcting journal; it is not available in this release.");
        Value.Require(d.N("SELECT COUNT(*) FROM Allocation a JOIN Invoice i ON i.Id=a.InvoiceId JOIN Voucher p ON p.Id=a.VoucherId WHERE i.VoucherId=@p0 AND p.ReversedBy IS NULL",id)==0,"Reverse allocated receipts/payments before cancelling this invoice.");
        var source=d.Table("SELECT * FROM Posting WHERE VoucherId=@p0",id);var lines=new List<Line>();long? Nullable(DataRow r,string k)=>r[k] is DBNull?null:Value.Long(r[k]);
        foreach(DataRow r in source.Rows){string account=Value.Text(r["Account"]);long amt=Value.Long(r["Amount"]);var emp=Nullable(r,"EmployeeId");var inst=Nullable(r,"InstrumentId");var site=Nullable(r,"SiteId");
            if(account is "STAFF" or "ADV" or "ELOAN" or "FD")Value.Require(Balance(d,account,employee:emp,site:site,instrument:inst)-amt>=0,"Later recoveries or closures depend on this voucher. Reverse them first.");
            if(account is "BLOAN" or "SALARY" or "EPF" or "ESIC" or "DEDUCTION")Value.Require(Balance(d,account,employee:account=="BLOAN"?null:emp,instrument:inst)-amt<=0,"Later payments depend on this liability. Reverse those payments first.");
            if(account=="FD"&&amt>0)Value.Require(d.N("SELECT COUNT(*) FROM BGFD WHERE FDId=@p0 AND ReleasedDay IS NULL",inst)==0,"Release the FD lien before reversing its creation.");
            lines.Add(new(account,-amt,Nullable(r,"PartyId"),emp,Nullable(r,"BankId"),site,inst,Nullable(r,"CategoryId")));
        }
        long reversal=Post(d,"Reversal",day,reason,lines,reversal:id);d.Exec("UPDATE Voucher SET ReversedBy=@p0 WHERE Id=@p1",reversal,id);
        d.Exec("UPDATE Invoice SET Cancelled=1 WHERE VoucherId=@p0",id);
        if(kind is "FD" or "Employee Loan")d.Exec("UPDATE Instrument SET Status='Cancelled' WHERE Id IN (SELECT InstrumentId FROM Posting WHERE VoucherId=@p0 AND InstrumentId IS NOT NULL)",id);
        Audit(d,"Voucher",id,"Reversed",null,new{reversal},reason);return reversal;
    }
    public void Reconcile(long posting,int statementDay,string reference,bool match)=>Write(d=>{var r=d.Row("SELECT p.*,v.Day,v.ReversedBy,v.ReversalOf FROM Posting p JOIN Voucher v ON v.Id=p.VoucherId WHERE p.Id=@p0 AND p.Account='BANK'",posting);Unlocked(d,(int)Value.Long(r["Day"]));Value.Require(statementDay>=Value.Long(r["Day"]),"Statement date cannot precede the bank transaction.");if(match){Value.Require(r["ReversedBy"] is DBNull&&r["ReversalOf"] is DBNull,"Reversed entries cannot be reconciled.");d.Exec("INSERT INTO Reconciliation(PostingId,StatementDay,Reference) VALUES(@p0,@p1,@p2) ON CONFLICT(PostingId) DO UPDATE SET StatementDay=excluded.StatementDay,Reference=excluded.Reference",posting,statementDay,reference);}else d.Exec("DELETE FROM Reconciliation WHERE PostingId=@p0",posting);Audit(d,"Posting",posting,match?"Reconciled":"Unmatched",null,new{statementDay,reference});return 0;});
    static readonly Dictionary<string,string> AttachmentTables=new(){["Invoice"]="Invoice",["Voucher"]="Voucher",["Employee"]="Employee",["Instrument"]="Instrument",["BG"]="BG"};
    public long Attach(string entity,long id,string name,byte[] bytes)=>Write(d=>{Value.Require(AttachmentTables.TryGetValue(entity,out var table),"Unsupported attachment destination.");Value.Require(d.N($"SELECT COUNT(*) FROM {table} WHERE Id=@p0",id)==1,"Select an existing record.");Value.Require(bytes.Length>0&&bytes.Length<=20_000_000,"Attachment must be between 1 byte and 20 MB.");name=System.IO.Path.GetFileName(name);string ext=System.IO.Path.GetExtension(name).ToLowerInvariant();Value.Require(new[]{".pdf",".png",".jpg",".jpeg",".txt",".csv",".xlsx",".docx"}.Contains(ext),"Use PDF, PNG, JPG, TXT, CSV, XLSX or DOCX attachments.");if(ext==".pdf")Value.Require(bytes.Length>=5&&System.Text.Encoding.ASCII.GetString(bytes,0,5)=="%PDF-","This is not a valid PDF file.");var aid=d.Insert("Attachment",new Dictionary<string,object?>{["Entity"]=entity,["EntityId"]=id,["Name"]=name,["Mime"]=ext,["Data"]=bytes,["SHA256"]=Convert.ToHexString(SHA256.HashData(bytes)),["CreatedUtc"]=DateTime.UtcNow.ToString("O")});Audit(d,entity,id,"Attachment added",null,new{Attachment=aid,name});return aid;});
    public void RemoveAttachment(long id,string reason)=>Write(d=>{Value.Require(!string.IsNullOrWhiteSpace(reason),"Enter a removal reason.");d.Exec("UPDATE Attachment SET Removed=1 WHERE Id=@p0",id);Audit(d,"Attachment",id,"Removed",null,null,reason);return 0;});
    public (string Name,byte[] Bytes) Attachment(long id){Authenticate();using var d=Open();var r=d.Row("SELECT * FROM Attachment WHERE Id=@p0 AND Removed=0",id);byte[] bytes=(byte[])r["Data"];Value.Require(Convert.ToHexString(SHA256.HashData(bytes))==Value.Text(r["SHA256"]),"Attachment integrity check failed.");return(Value.Text(r["Name"]),bytes);}
    public string Integrity(){Authenticate();using var d=Open();return ValidateDatabase(d);}
    static string ValidateDatabase(Db d)
    {
        Value.Require(d.N("PRAGMA application_id")==1179472689&&d.N("PRAGMA user_version")==2,"Backup belongs to another application or database version.");
        Value.Require(Value.Text(d.Scalar("PRAGMA integrity_check"))=="ok","Database integrity check failed.");Value.Require(d.Table("PRAGMA foreign_key_check").Rows.Count==0,"Database has broken record relationships.");
        Value.Require(d.N("SELECT COUNT(*) FROM (SELECT v.Id FROM Voucher v LEFT JOIN Posting p ON p.VoucherId=v.Id GROUP BY v.Id HAVING COUNT(p.Id)<2 OR SUM(p.Amount)<>0)")==0,"Database contains an unbalanced voucher.");
        Value.Require(d.N("SELECT COUNT(*) FROM Company WHERE Id=1")==1,"Company configuration is missing.");return "Database integrity, relationships and voucher balances are valid.";
    }
    public string Backup(string folder)
    {
        Authenticate();Directory.CreateDirectory(folder);string path=System.IO.Path.Combine(folder,"BusinessFinance_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N")[..6]+".db");
        using(var source=Open())using(var dest=new Db(path)){source.Connection.BackupDatabase(dest.Connection);ValidateDatabase(dest);}
        using(var d=Open()){d.Exec("INSERT INTO BackupHistory(TimeUtc,File,Status) VALUES(@p0,@p1,'Success')",DateTime.UtcNow.ToString("O"),path);Audit(d,"Backup",null,"Created",null,new{path});}return path;
    }
    public string Restore(string backup)
    {
        Authenticate();Value.Require(System.IO.Path.GetFullPath(backup)!=Path,"Select a separate backup file.");using(var candidate=new Db(backup,true)){ValidateDatabase(candidate);Value.Require(candidate.N("SELECT COUNT(*) FROM AppUser")>0,"Backup has no administrator account.");}
        var protectedCopy=Backup(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,"BeforeRestore"));string temp=Path+".restore-"+Guid.NewGuid().ToString("N");File.Copy(backup,temp);
        using(var verify=new Db(temp)){ValidateDatabase(verify);}
        File.Move(temp,Path,true);Logout();using(var restored=Open())Audit(restored,"Backup",null,"Restored",null,new{backup,protectedCopy},"User-confirmed restore; login required");return protectedCopy;
    }
    public void SetSetting(string key,string value)=>Write(d=>{Value.Require(key is "BackupFolder" or "BackupMinutes" or "FinancialYear" || key.StartsWith("Prefix."),"Unsupported setting.");if(key=="BackupMinutes")Value.Require(int.TryParse(value,out int n)&&n>=5&&n<=10080,"Backup interval must be 5 to 10,080 minutes.");if(key.StartsWith("Prefix."))Value.Require(value.Length is >0 and <=20&&value.All(c=>char.IsLetterOrDigit(c)||c=='-'),"Voucher prefix should contain up to 20 letters, numbers or hyphens.");var old=d.Scalar("SELECT Value FROM Setting WHERE Key=@p0",key);d.Exec("INSERT INTO Setting VALUES(@p0,@p1) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value",key,value);Audit(d,"Setting",null,"Changed",old,new{key,value});return 0;});
}
