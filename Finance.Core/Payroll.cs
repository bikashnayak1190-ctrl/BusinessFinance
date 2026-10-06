using System.Data;
using System.Text.Json;
namespace Finance.Core;
public record StatutoryInput(int EffectiveDay,decimal BasicPercent=50,decimal EPFRate=12,long EPFCeiling=1500000,bool CapEPF=true,string EPFBasis="Basic",decimal ESICRate=0.75m,long ESICCeiling=2100000,string ESICBasis="Gross",string EPFRounding="Rupee",string ESICRounding="Ceiling",string SalaryRounding="Paise");
public record PayrollInput(long Employee,decimal Absent=0,long Advance=0,Dictionary<long,long>? Loans=null,long OT=0,long Bonus=0,long Arrear=0,long OtherAddition=0,long OtherDeduction=0,bool ESICContinuation=false);
public record PayrollResult(long Employee,string Name,long MonthlySalary,int CalendarDays,int Sundays,int WorkingDays,int EligibleDays,decimal Absent,decimal PayableDays,long Earned,long Basic,long Allowance,long EPFWage,long EPF,long ESICWage,long ESIC,long AdvanceOpening,long Advance,long LoanRecovery,long OT,long Bonus,long Arrear,long OtherAddition,long OtherDeduction,long Net,long Rounding,PayrollInput Input,long SettingId);
public partial class Engine
{
    public long SaveStatutory(StatutoryInput s)=>Write(d=>{
        Value.Require(s.BasicPercent is >=0 and <=100&&s.EPFRate is >=0 and <=100&&s.ESICRate is >=0 and <=100&&s.EPFCeiling>=0&&s.ESICCeiling>=0,"Enter valid statutory percentages and ceilings.");
        Value.Require(DateOnly.FromDayNumber(s.EffectiveDay).Day==1,"Statutory settings take effect on the first day of a month.");
        Value.Require(s.EPFBasis is "Basic" or "Gross" && s.ESICBasis is "Basic" or "Gross","Choose Basic or Gross as the contribution basis.");
        foreach(var rule in new[]{s.EPFRounding,s.ESICRounding,s.SalaryRounding})Value.Round(1,rule);
        Value.Require(d.N("SELECT COUNT(*) FROM Payroll WHERE Status='Finalized' AND Day>=@p0",s.EffectiveDay)==0,"Reopen affected payroll periods before adding a retrospective statutory setting.");
        var id=d.Insert("Statutory",new Dictionary<string,object?>{["EffectiveDay"]=s.EffectiveDay,["BasicPercent"]=s.BasicPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),["EPFRate"]=s.EPFRate.ToString(System.Globalization.CultureInfo.InvariantCulture),["EPFCeiling"]=s.EPFCeiling,["CapEPF"]=s.CapEPF?1:0,["EPFBasis"]=s.EPFBasis,["ESICRate"]=s.ESICRate.ToString(System.Globalization.CultureInfo.InvariantCulture),["ESICCeiling"]=s.ESICCeiling,["ESICBasis"]=s.ESICBasis,["EPFRounding"]=s.EPFRounding,["ESICRounding"]=s.ESICRounding,["SalaryRounding"]=s.SalaryRounding});Audit(d,"Statutory",id,"Created",null,s);return id;
    });
    internal List<PayrollResult> Calculate(Db d,int year,int month,List<PayrollInput> inputs)
    {
        Value.Require(year is >=1900 and <=2200 && month is >=1 and <=12,"Choose a valid payroll month.");
        var start=new DateOnly(year,month,1);int days=DateTime.DaysInMonth(year,month);var end=start.AddDays(days-1);int sundays=Enumerable.Range(0,days).Count(i=>start.AddDays(i).DayOfWeek==DayOfWeek.Sunday);
        var setting=d.Row("SELECT * FROM Statutory WHERE EffectiveDay<=@p0 ORDER BY EffectiveDay DESC LIMIT 1",start.DayNumber);
        Value.Require(inputs.Count>0&&inputs.Select(x=>x.Employee).Distinct().Count()==inputs.Count,"Load each employee once.");var output=new List<PayrollResult>();
        foreach(var x in inputs){
            var emp=d.Row("SELECT * FROM Employee WHERE Id=@p0",x.Employee);int begin=Math.Max(start.DayNumber,(int)Value.Long(emp["JoinDay"]));int last=Math.Min(end.DayNumber,emp["LeaveDay"] is DBNull?end.DayNumber:(int)Value.Long(emp["LeaveDay"]));int eligible=Math.Max(0,last-begin+1);
            Value.Require(eligible>0,"An employee has no eligible days in this payroll month.");Value.Require(x.Absent>=0&&x.Absent<=eligible,"Absent days cannot exceed eligible calendar days.");
            Value.Require(new[]{x.Advance,x.OT,x.Bonus,x.Arrear,x.OtherAddition,x.OtherDeduction}.All(a=>a>=0),"Payroll additions and deductions cannot be negative.");
            long salary=Value.Long(emp["Salary"]);decimal payable=eligible-x.Absent;long earned=Value.Money(Value.Rupees(salary)/days*payable);long basic=Value.Money(Value.Rupees(earned)*Value.Dec(setting["BasicPercent"])/100);long allowance=earned-basic;
            long gross=checked(earned+x.OT+x.Bonus+x.Arrear+x.OtherAddition);
            long epfWage=Value.Text(setting["EPFBasis"])=="Basic"?basic:gross;if(Value.Long(setting["CapEPF"])==1)epfWage=Math.Min(epfWage,Value.Long(setting["EPFCeiling"]));
            long epf=Value.Long(emp["EPF"])==1?Value.Round(Value.Money(Value.Rupees(epfWage)*Value.Dec(setting["EPFRate"])/100),Value.Text(setting["EPFRounding"])):0;
            bool esicEligible=salary<=Value.Long(setting["ESICCeiling"])||x.ESICContinuation;
            long esicWage=Value.Text(setting["ESICBasis"])=="Basic"?basic:gross;
            long esic=Value.Long(emp["ESIC"])==1&&esicEligible?Value.Round(Value.Money(Value.Rupees(esicWage)*Value.Dec(setting["ESICRate"])/100),Value.Text(setting["ESICRounding"])):0;
            long opening=Balance(d,"ADV",employee:x.Employee,through:end.DayNumber);Value.Require(x.Advance<=opening,"Employee advance deduction exceeds the outstanding balance.");
            long loanTotal=0;foreach(var (id,amount) in x.Loans??[]){var loan=d.Row("SELECT * FROM Instrument WHERE Id=@p0",id);Value.Require(Value.Text(loan["Kind"])=="Employee Loan"&&Value.Long(loan["EmployeeId"])==x.Employee,"Select a loan belonging to this employee.");Value.Require(loan["RecoveryDay"] is DBNull||Value.Long(loan["RecoveryDay"])<=end.DayNumber,"Loan recovery has not started yet.");Recover(d,"ELOAN",amount,employee:x.Employee,instrument:id,day:end.DayNumber);loanTotal=checked(loanTotal+amount);}
            long raw=checked(gross-epf-esic-x.Advance-loanTotal-x.OtherDeduction);Value.Require(raw>=0,"Deductions exceed earnings. Reduce recovery; net salary cannot be negative.");long net=Value.Round(raw,Value.Text(setting["SalaryRounding"]));
            output.Add(new(x.Employee,Value.Text(emp["Name"]),salary,days,sundays,days-sundays,eligible,x.Absent,payable,earned,basic,allowance,epfWage,epf,esicWage,esic,opening,x.Advance,loanTotal,x.OT,x.Bonus,x.Arrear,x.OtherAddition,x.OtherDeduction,net,net-raw,x,Value.Long(setting["Id"])));
        }return output;
    }
    public List<PayrollResult> Calculate(int year,int month,List<PayrollInput> inputs){Authenticate();using var d=Open();return Calculate(d,year,month,inputs);}
    public long FinalizePayroll(int year,int month,List<PayrollInput> inputs)=>Write(d=>{
        Value.Require(d.N("SELECT COUNT(*) FROM Payroll WHERE Year=@p0 AND Month=@p1 AND Status='Finalized'",year,month)==0,"This payroll is already finalized.");
        var rows=Calculate(d,year,month,inputs);int day=new DateOnly(year,month,DateTime.DaysInMonth(year,month)).DayNumber;var lines=new List<Line>();
        foreach(var r in rows){lines.Add(new("WAGES",r.Earned+r.OT+r.Bonus+r.Arrear+r.OtherAddition,Employee:r.Employee));lines.Add(new("ROUND",r.Rounding));lines.Add(new("EPF",-r.EPF,Employee:r.Employee));lines.Add(new("ESIC",-r.ESIC,Employee:r.Employee));lines.Add(new("DEDUCTION",-r.OtherDeduction,Employee:r.Employee));lines.Add(new("ADV",-r.Advance,Employee:r.Employee));lines.Add(new("SALARY",-r.Net,Employee:r.Employee));foreach(var (loan,amt) in r.Input.Loans??[])lines.Add(new("ELOAN",-amt,Employee:r.Employee,Instrument:loan));}
        var voucher=Post(d,"Payroll",day,$"Payroll {month:00}-{year}",lines);
        int revision=(int)d.N("SELECT COALESCE(MAX(Revision),0)+1 FROM Payroll WHERE Year=@p0 AND Month=@p1",year,month);
        long id=d.Insert("Payroll",new Dictionary<string,object?>{["Year"]=year,["Month"]=month,["Revision"]=revision,["SettingId"]=rows[0].SettingId,["Status"]="Finalized",["Day"]=day,["VoucherId"]=voucher});
        foreach(var r in rows)d.Exec("INSERT INTO PayrollRow(PayrollId,EmployeeId,Snapshot,Net) VALUES(@p0,@p1,@p2,@p3)",id,r.Employee,Value.Json(r),r.Net);
        Audit(d,"Payroll",id,"Finalized",null,rows);return id;
    });
    public void ReopenPayroll(long id,string reason)=>Write(d=>{
        var p=d.Row("SELECT * FROM Payroll WHERE Id=@p0",id);Value.Require(Value.Text(p["Status"])=="Finalized","Select finalized payroll.");
        Value.Require(d.N("SELECT COUNT(*) FROM SalaryAllocation a JOIN PayrollRow r ON r.Id=a.PayrollRowId JOIN Voucher v ON v.Id=a.VoucherId WHERE r.PayrollId=@p0 AND v.ReversedBy IS NULL",id)==0,"Reverse salary payments before reopening payroll.");
        Reverse(d,Value.Long(p["VoucherId"]),(int)Value.Long(p["Day"]),reason,true);d.Exec("UPDATE Payroll SET Status='Reopened' WHERE Id=@p0",id);Audit(d,"Payroll",id,"Reopened",null,null,reason);return 0;
    });
}
