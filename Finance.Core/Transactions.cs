using System.Data;
namespace Finance.Core;
public record InvoiceInput(string Kind,string Number,long Party,int Day,int DueDay,string PlaceOfSupply,string Reference,string Remarks,List<InvoiceItem> Lines,bool ITC=true,string PurchaseAccount="PURCHASE",bool RoundTotal=false,bool ManualRateVerified=false);
public record InvoiceItem(string Description,string HSN,decimal Quantity,string Unit,long Rate,long Discount,decimal GSTRate=18,long? Item=null);
public record AllocationInput(long Invoice,long Amount);
public record Movement(string Head,int Day,long Amount,long Bank,long? Party=null,long? Employee=null,long? Site=null,long? Instrument=null,long? PayrollRow=null,long? Category=null,long? BG=null,long Interest=0,long Charges=0,string Mode="NEFT",string Reference="",string Remarks="",List<AllocationInput>? Allocations=null,string CounterAccount="",bool SettleOpeningBalance=false);
public partial class Engine
{
    public static readonly IReadOnlyDictionary<string,string[]> MasterFields = new Dictionary<string,string[]>{
        ["Party"]=["Kind","Code","Name","Address","GSTIN","StateCode","ContactPerson","Mobile","Email","CreditDays","Active","Remarks"],
        ["Employee"]=["Code","Name","Designation","Department","JoinDay","LeaveDay","Salary","EPF","ESIC","UAN","IPNumber","BankName","AccountNumber","IFSC","Active","Remarks"],
        ["Bank"]=["Name","Branch","AccountNumber","AccountType","IFSC","Cash","Active"],
        ["Site"]=["Code","Name","Location","PartyId","StartDay","EndDay","Status","Remarks"],
        ["Item"]=["Code","Name","Description","HSN","Unit","Rate","GSTRate","Active"],
        ["Category"]=["Name"],
        ["Company"]=["Name","Address","GSTIN","StateCode","PAN","Mobile","Email","Website","Currency","ShowLogo","LogoPosition"]};
    public long SaveMaster(string table,Dictionary<string,object?> values,long? id=null)=>Write(d=>{
        Value.Require(MasterFields.ContainsKey(table)&&values.Keys.All(x=>MasterFields[table].Contains(x)),"Unsupported master field.");
        var v=new Dictionary<string,object?>(values);var old=id.HasValue?d.Row($"SELECT * FROM {table} WHERE Id=@p0",id):null;
        object? Get(string key)=>v.TryGetValue(key,out var x)?x:old?.Table.Columns.Contains(key)==true?old[key]:null;
        Value.Require(!string.IsNullOrWhiteSpace(Value.Text(Get("Name"))),"Name is required.");
        if(MasterFields[table].Contains("Code"))Value.Require(!string.IsNullOrWhiteSpace(Value.Text(Get("Code"))),"Code is required.");
        if(table=="Party"){Value.Require(Value.Text(Get("Kind")) is "Customer" or "Supplier","Select a party type.");if(old!=null)Value.Require(Value.Text(old["Kind"])==Value.Text(Get("Kind")),"A party type cannot be changed.");}
        if(table=="Employee"){Value.Require(Value.Long(Get("Salary"))>=0,"Salary cannot be negative.");Value.Require(Get("JoinDay") is not null,"Joining date is required.");}
        if(table=="Item")Value.Require(Value.Long(Get("Rate"))>=0 && Value.Dec(Get("GSTRate")) is >=0 and <=100,"Enter a valid rate and GST percentage.");
        if(table=="Company")Value.Require(Value.Text(Get("Currency")) is "" or "INR","This release supports INR as the company currency.");
        if(table=="Bank")Value.Require(!string.IsNullOrWhiteSpace(Value.Text(Get("AccountNumber"))),"Account number is required. For cash, use CASH.");
        if(MasterFields[table].Contains("GSTIN")){var gst=Value.Text(Get("GSTIN")).Trim().ToUpperInvariant();Value.Require(gst==""||System.Text.RegularExpressions.Regex.IsMatch(gst,"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][A-Z0-9]Z[A-Z0-9]$"),"Enter a valid 15-character GSTIN, or leave it blank.");if(gst!="")Value.Require(gst[..2]==Value.Text(Get("StateCode")),"GSTIN state code and selected state must agree.");}
        if(id.HasValue){var args=v.Values.Append<object?>(id).ToArray();d.Exec($"UPDATE {table} SET {string.Join(',',v.Keys.Select((x,i)=>x+"=@p"+i))} WHERE Id=@p{v.Count}",args);}else id=d.Insert(table,v);
        Audit(d,table,id,old==null?"Created":"Changed",old?.ItemArray,v);return id.Value;
    });
    public void SetLogo(byte[]? data)=>Write(d=>{Value.Require(data==null||data.Length<=5_000_000,"Logo must be smaller than 5 MB.");d.Exec("UPDATE Company SET Logo=@p0 WHERE Id=1",data);Audit(d,"Company",1,data==null?"Logo removed":"Logo changed");return 0;});
    static void Active(Db d,string table,long? id){Value.Require(id>0,$"Select a {table.ToLowerInvariant()}.");Value.Require(d.N($"SELECT COUNT(*) FROM {table} WHERE Id=@p0 AND Active=1",id)==1,$"The selected {table.ToLowerInvariant()} is inactive or missing.");}
    public long SaveInvoice(InvoiceInput x,bool post=true)=>Write(d=>{
        Value.Require(x.Kind is "Sales" or "Purchase","Select Sales or Purchase.");Unlocked(d,x.Day);Active(d,"Party",x.Party);
        Value.Require(!string.IsNullOrWhiteSpace(x.Number),"Manually enter the invoice number.");Value.Require(x.DueDay>=x.Day,"Due date cannot precede the invoice date.");
        var party=d.Row("SELECT * FROM Party WHERE Id=@p0",x.Party);Value.Require(Value.Text(party["Kind"])==(x.Kind=="Sales"?"Customer":"Supplier"),"Choose the correct customer or supplier.");
        var co=d.Row("SELECT * FROM Company WHERE Id=1");string state=Value.Text(co["StateCode"]);
        Value.Require(state.Length==2&&x.PlaceOfSupply.Length==2&&Value.Text(party["StateCode"]).Length==2,"Configure company, party and place-of-supply state codes before invoicing.");
        Value.Require(x.Lines.Count>0,"Add at least one invoice line.");Value.Require(x.PurchaseAccount is "PURCHASE" or "EXPENSE" or "ASSET","Select the purchase posting account.");
        bool intra=(x.Kind=="Sales"?state:Value.Text(party["StateCode"]))==x.PlaceOfSupply;
        var calculated=new List<Dictionary<string,object?>>();long taxable=0,cgst=0,sgst=0,igst=0;
        foreach(var line in x.Lines){
            Value.Require(!string.IsNullOrWhiteSpace(line.Description)&&line.Quantity>0&&line.Rate>=0&&line.Discount>=0,"Each line requires a description, positive quantity and non-negative rate/discount.");
            Value.Require(line.GSTRate>=0&&line.GSTRate<=100,"GST rate must be between 0 and 100.");Value.Require(new decimal[]{0,5,12,18,28}.Contains(line.GSTRate)||x.ManualRateVerified,"Verify the manual GST rate before saving.");
            long gross=Value.Money(Value.Rupees(line.Rate)*line.Quantity);Value.Require(line.Discount<=gross,"Discount cannot exceed the line value.");long basis=gross-line.Discount;
            long cg=intra?Value.Money(Value.Rupees(basis)*line.GSTRate/200):0;long sg=cg;long ig=intra?0:Value.Money(Value.Rupees(basis)*line.GSTRate/100);
            taxable=checked(taxable+basis);cgst=checked(cgst+cg);sgst=checked(sgst+sg);igst=checked(igst+ig);
            calculated.Add(new(){["Description"]=line.Description,["HSN"]=line.HSN,["Quantity"]=line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),["Unit"]=line.Unit,["Rate"]=line.Rate,["Discount"]=line.Discount,["GSTRate"]=line.GSTRate.ToString(System.Globalization.CultureInfo.InvariantCulture),["Taxable"]=basis,["CGST"]=cg,["SGST"]=sg,["IGST"]=ig,["Total"]=basis+cg+sg+ig,["ItemId"]=line.Item});
        }
        long raw=checked(taxable+cgst+sgst+igst), total=x.RoundTotal?Value.Round(raw,"Rupee"):raw;
        Value.Require(total>0,"Invoice total must be positive.");
        var id=d.Insert("Invoice",new Dictionary<string,object?>{["Kind"]=x.Kind,["FY"]=Value.FY(x.Day),["Number"]=x.Number.Trim(),["PartyId"]=x.Party,["Day"]=x.Day,["DueDay"]=x.DueDay,["PlaceOfSupply"]=x.PlaceOfSupply,["CompanyState"]=state,["PartyState"]=party["StateCode"],["PartyGSTIN"]=party["GSTIN"],["CompanyGSTIN"]=co["GSTIN"],["Reference"]=x.Reference,["Remarks"]=x.Remarks,["Taxable"]=taxable,["CGST"]=cgst,["SGST"]=sgst,["IGST"]=igst,["RoundOff"]=total-raw,["Total"]=total,["ITC"]=x.ITC?1:0,["PurchaseAccount"]=x.PurchaseAccount});
        foreach(var l in calculated){l["InvoiceId"]=id;d.Insert("InvoiceLine",l);}Audit(d,"Invoice",id,"Created",null,x);if(post)PostInvoice(d,id);return id;
    });
    public void PostInvoice(long id)=>Write(d=>{PostInvoice(d,id);return 0;});
    void PostInvoice(Db d,long id){var r=d.Row("SELECT * FROM Invoice WHERE Id=@p0",id);Value.Require(r["VoucherId"] is DBNull && Value.Long(r["Cancelled"])==0,"Invoice is already posted or cancelled.");
        long n(string s)=>Value.Long(r[s]);bool sales=Value.Text(r["Kind"])=="Sales";var lines=new List<Line>();
        if(sales){lines.Add(new("AR",n("Total"),Party:n("PartyId")));lines.Add(new("SALES",-n("Taxable")));lines.Add(new("OCGST",-n("CGST")));lines.Add(new("OSGST",-n("SGST")));lines.Add(new("OIGST",-n("IGST")));lines.Add(new("ROUND",-n("RoundOff")));}
        else {bool itc=n("ITC")==1;lines.Add(new("AP",-n("Total"),Party:n("PartyId")));lines.Add(new(Value.Text(r["PurchaseAccount"]),n("Taxable")+(itc?0:n("CGST")+n("SGST")+n("IGST"))));if(itc){lines.Add(new("ICGST",n("CGST")));lines.Add(new("ISGST",n("SGST")));lines.Add(new("IIGST",n("IGST")));}lines.Add(new("ROUND",n("RoundOff")));}
        long voucher=Post(d,Value.Text(r["Kind"]),(int)n("Day"),Value.Text(r["Number"]),lines,"invoice:"+id);d.Exec("UPDATE Invoice SET VoucherId=@p0 WHERE Id=@p1",voucher,id);
    }
    internal long Outstanding(Db d,long invoice)=>d.N("SELECT i.Total-COALESCE((SELECT SUM(a.Amount) FROM Allocation a JOIN Voucher v ON v.Id=a.VoucherId WHERE a.InvoiceId=i.Id AND v.ReversedBy IS NULL),0) FROM Invoice i JOIN Voucher iv ON iv.Id=i.VoucherId WHERE i.Id=@p0 AND i.Cancelled=0 AND iv.ReversedBy IS NULL",invoice);
    public long Outstanding(long invoice){Authenticate();using var d=Open();return Outstanding(d,invoice);}
    internal void Recover(Db d,string account,long amount,long? employee=null,long? site=null,long? instrument=null,long? party=null,int? day=null){Value.Require(amount>=0 && amount<=Balance(d,account,party:party,employee:employee,site:site,instrument:instrument,through:day),"Recovery exceeds the available outstanding balance on this date.");}
    public long Transfer(int day,long from,long to,long amount,string reference)=>Write(d=>{Value.Require(from!=to,"Source and destination accounts must differ.");Value.Require(amount>0,"Amount must be positive.");Active(d,"Bank",from);Active(d,"Bank",to);return Post(d,"Bank Transfer",day,reference,[new("BANK",-amount,Bank:from),new("BANK",amount,Bank:to)]);});
    public long SiteExpense(int day,long employee,long site,long category,long amount,long? bank,string remarks)=>Write(d=>{Value.Require(amount>0,"Expense must be positive.");Active(d,"Employee",employee);Value.Require(d.N("SELECT COUNT(*) FROM Site WHERE Id=@p0",site)==1,"Select a site.");if(bank==null)Recover(d,"STAFF",amount,employee,site,day:day);else Active(d,"Bank",bank);return Post(d,"Site Expense",day,remarks,[new("EXPENSE",amount,Employee:employee,Site:site,Category:category),bank==null?new("STAFF",-amount,Employee:employee,Site:site):new("BANK",-amount,Bank:bank)]);});
    public static readonly string[] ReceiptHeads=["Customer Receipt","Staff Advance Refund","Employee Advance Refund","Employee Loan Repayment","Supplier Refund","Customer Advance","Business Loan Received","Interest Received","Capital Introduced","Security Deposit Refund","Other Income","Other Receipt"];
    public static readonly string[] PaymentHeads=["Supplier Payment","Staff Advance","Employee Advance","Expense Payment","Salary/Wages","Business Loan Repayment","BG Charges","Tax/Statutory Payment","Customer Refund","Bank Charges","Interest Payment","Other Payment"];
    public long Move(Movement x,bool receipt)=>Write(d=>{
        Value.Require((receipt?ReceiptHeads:PaymentHeads).Contains(x.Head),"Choose a valid receipt/payment head.");Value.Require(x.Amount>0&&x.Interest>=0&&x.Charges>=0,"Enter positive amounts.");Active(d,"Bank",x.Bank);
        if(x.Employee!=null)Active(d,"Employee",x.Employee);if(x.Party!=null)Active(d,"Party",x.Party);
        var lines=new List<Line>{new("BANK",receipt?x.Amount:-x.Amount,Bank:x.Bank)};long other=receipt?-x.Amount:x.Amount;
        string account=x.Head switch{
            "Customer Receipt"=>"AR","Customer Refund"=>x.CounterAccount=="CUSTADV"?"CUSTADV":"AR","Supplier Payment" or "Supplier Refund"=>"AP", "Staff Advance" or "Staff Advance Refund"=>"STAFF", "Employee Advance" or "Employee Advance Refund"=>"ADV","Employee Loan Repayment"=>"ELOAN","Business Loan Received" or "Business Loan Repayment"=>"BLOAN","Customer Advance"=>"CUSTADV","Interest Received"=>"INTERESTIN","Capital Introduced"=>"CAPITAL","Security Deposit Refund"=>"DEPOSIT","Other Income"=>"INCOME","Expense Payment"=>"EXPENSE","Salary/Wages"=>"SALARY","BG Charges" or "Bank Charges"=>"CHARGES","Interest Payment"=>"INTEREST","Tax/Statutory Payment"=>x.Reference is "EPF" or "ESIC" or "OCGST" or "OSGST" or "OIGST" or "DEDUCTION"?x.Reference:throw new BusinessException("Select the statutory payable account."),"Other Receipt" or "Other Payment"=>new[]{"INCOME","CAPITAL","ASSET","DEPOSIT","EXPENSE","CHARGES","INTEREST"}.Contains(x.CounterAccount)?x.CounterAccount:throw new BusinessException("Select the counter ledger for this transaction."),_=>throw new BusinessException("Choose a supported transaction head.")};
        if(account is "AR" or "AP" or "CUSTADV"){var p=d.Row("SELECT Kind FROM Party WHERE Id=@p0",x.Party);Value.Require(Value.Text(p[0])==(account=="AP"?"Supplier":"Customer"),"Select the correct party type.");}
        if(x.Head is "Staff Advance Refund" or "Employee Advance Refund" or "Employee Loan Repayment")Recover(d,account,x.Amount,x.Employee,x.Head=="Staff Advance Refund"?x.Site:null,x.Instrument,day:x.Day);
        if(account=="STAFF")Value.Require(x.Site>0,"Select the site/project for this advance.");
        if(account is "ELOAN" or "BLOAN"){
            var inst=d.Row("SELECT * FROM Instrument WHERE Id=@p0",x.Instrument);Value.Require(Value.Text(inst["Kind"])==(account=="ELOAN"?"Employee Loan":"Business Loan"),"Select the correct loan type.");Value.Require(x.Day>=Value.Long(inst["Day"]),"The transaction predates the loan.");
            if(account=="ELOAN")Value.Require(Value.Long(inst["EmployeeId"])==x.Employee,"Loan belongs to a different employee.");
            if(x.Head=="Business Loan Received")Value.Require(-Balance(d,"BLOAN",instrument:x.Instrument)+x.Amount<=Value.Long(inst["Principal"]),"Disbursement exceeds the sanctioned amount.");
        }
        long principal=x.Amount;
        if(x.Head=="Business Loan Repayment"){
            principal=x.Amount-x.Interest-x.Charges;Value.Require(principal>=0,"Interest and charges cannot exceed the payment.");Value.Require(principal<=-Balance(d,"BLOAN",instrument:x.Instrument,through:x.Day),"Principal repayment exceeds the loan outstanding.");lines.Add(new("INTEREST",x.Interest,Instrument:x.Instrument));lines.Add(new("CHARGES",x.Charges,Instrument:x.Instrument));other=principal;
        }
        if(x.Head=="Salary/Wages"){
            var row=d.Row("SELECT r.*,p.Day FROM PayrollRow r JOIN Payroll p ON p.Id=r.PayrollId WHERE r.Id=@p0 AND p.Status='Finalized'",x.PayrollRow);
            Value.Require(Value.Long(row["EmployeeId"])==x.Employee,"Choose payroll for this employee.");Value.Require(x.Day>=Value.Long(row["Day"]),"Salary payment cannot precede payroll finalization.");
            Value.Require(x.Amount<=SalaryDue(d,x.PayrollRow!.Value),"Payment exceeds the remaining salary due.");
        }
        if(x.Head=="Customer Refund")Value.Require(Balance(d,account,party:x.Party,through:x.Day)<=-x.Amount,"Customer has no sufficient receivable credit balance. Customer advances require a separately reviewed adjustment.");
        if(x.Head=="Supplier Refund")Value.Require(Balance(d,"AP",party:x.Party,through:x.Day)>=x.Amount,"Supplier has no sufficient debit balance for this refund.");
        if(x.Head=="Tax/Statutory Payment")Value.Require(x.Amount<=-Balance(d,account,through:x.Day),"Payment exceeds the selected statutory liability.");
        lines.Add(new(account,other,Party:x.Party,Employee:x.Employee,Site:account=="STAFF"?x.Site:null,Instrument:x.Instrument,Category:x.Category));
        var allocations=x.Allocations??[];
        if(x.Head is "Customer Receipt" or "Supplier Payment"){
            if(x.SettleOpeningBalance){Value.Require(allocations.Count==0,"Do not allocate invoices when settling an opening balance.");long invoiceDue=d.N("SELECT COALESCE(SUM(i.Total-COALESCE((SELECT SUM(a.Amount) FROM Allocation a JOIN Voucher av ON av.Id=a.VoucherId WHERE a.InvoiceId=i.Id AND av.ReversedBy IS NULL),0)),0) FROM Invoice i JOIN Voucher iv ON iv.Id=i.VoucherId WHERE i.PartyId=@p0 AND i.Kind=@p1 AND i.Cancelled=0 AND iv.ReversedBy IS NULL",x.Party,receipt?"Sales":"Purchase");long ledgerDue=Balance(d,account,party:x.Party,through:x.Day)*(receipt?1:-1);Value.Require(x.Amount<=ledgerDue-invoiceDue,"Amount exceeds the unallocated opening balance. Allocate against invoices instead.");}
            else {Value.Require(allocations.Count>0&&allocations.Sum(a=>a.Amount)==x.Amount,"Allocate the full amount to one or more invoices. Use Customer Advance for unallocated customer money.");Value.Require(allocations.Select(a=>a.Invoice).Distinct().Count()==allocations.Count,"An invoice cannot be allocated twice.");
            foreach(var a in allocations){var inv=d.Row("SELECT * FROM Invoice WHERE Id=@p0",a.Invoice);Value.Require(Value.Long(inv["PartyId"])==x.Party && Value.Text(inv["Kind"])==(receipt?"Sales":"Purchase"),"An allocated invoice belongs to a different party/type.");Value.Require(x.Day>=Value.Long(inv["Day"]),"Receipt/payment cannot precede its invoice.");Value.Require(a.Amount>0&&a.Amount<=Outstanding(d,a.Invoice),"An allocation exceeds the invoice outstanding.");}}
        }else Value.Require(allocations.Count==0,"Invoice allocation is only available for customer receipts and supplier payments.");
        var voucher=Post(d,receipt?"Receipt":"Payment",x.Day,$"{x.Head} | {x.Mode} | {x.Reference} | {x.Remarks}",lines);
        foreach(var a in allocations)d.Exec("INSERT INTO Allocation(VoucherId,InvoiceId,Amount) VALUES(@p0,@p1,@p2)",voucher,a.Invoice,a.Amount);
        if(x.Head=="Salary/Wages")d.Exec("INSERT INTO SalaryAllocation(VoucherId,PayrollRowId,Amount) VALUES(@p0,@p1,@p2)",voucher,x.PayrollRow,x.Amount);
        if(x.Head=="BG Charges"){Value.Require(x.BG>0,"Select the bank guarantee.");d.Exec("INSERT INTO BGCharge VALUES(@p0,@p1)",voucher,x.BG);}
        return voucher;
    });
    internal long SalaryDue(Db d,long row)=>d.N("SELECT r.Net-COALESCE((SELECT SUM(a.Amount) FROM SalaryAllocation a JOIN Voucher v ON v.Id=a.VoucherId WHERE a.PayrollRowId=r.Id AND v.ReversedBy IS NULL),0) FROM PayrollRow r WHERE r.Id=@p0",row);

    public long ApplyCustomerAdvance(int day,long party,List<AllocationInput> allocations,string reference)=>Write(d=>{
        long total=allocations.Sum(a=>a.Amount);Value.Require(total>0&&allocations.Select(a=>a.Invoice).Distinct().Count()==allocations.Count,"Select distinct invoices and positive allocations.");
        Value.Require(total<=-Balance(d,"CUSTADV",party:party,through:day),"Allocation exceeds the available customer advance.");
        foreach(var a in allocations){var r=d.Row("SELECT * FROM Invoice WHERE Id=@p0",a.Invoice);Value.Require(Value.Text(r["Kind"])=="Sales"&&Value.Long(r["PartyId"])==party&&day>=Value.Long(r["Day"])&&a.Amount>0&&a.Amount<=Outstanding(d,a.Invoice),"Check the invoice party, date and outstanding allocation.");}
        long v=Post(d,"Advance Adjustment",day,reference,[new("CUSTADV",total,Party:party),new("AR",-total,Party:party)]);foreach(var a in allocations)d.Exec("INSERT INTO Allocation(VoucherId,InvoiceId,Amount) VALUES(@p0,@p1,@p2)",v,a.Invoice,a.Amount);return v;
    });
}
