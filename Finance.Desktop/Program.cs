using Finance.Core;
namespace Finance.Desktop;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2 && args[0]=="--smoke-test"){Environment.ExitCode=SmokeChecks.Run(System.IO.Path.GetFullPath(args[1]));return;}
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException+=(_,e)=>Ui.Error(e.Exception);
        string folder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BikashRanjanNayak","BusinessFinance");Directory.CreateDirectory(folder);
        using var mutex=new Mutex(true,"Local\\BusinessFinanceSingleUser",out bool first);if(!first){MessageBox.Show("Business Finance is already running.");return;}
        try{var engine=new Engine(System.IO.Path.Combine(folder,"business.db"));using var login=new Login(engine);if(login.ShowDialog()==DialogResult.OK)Application.Run(new MainForm(engine));}catch(Exception ex){Ui.Error(ex);}
    }
}
internal static class Ui
{
    public const string Credit="Designed & Developed by Bikash Ranjan Nayak";
    public static readonly Color Navy=Color.FromArgb(19,36,57),Teal=Color.FromArgb(0,110,112),Back=Color.FromArgb(243,246,249);
    public static void Error(Exception ex){if(ex is BusinessException)MessageBox.Show(ex.Message,"Check this entry",MessageBoxButtons.OK,MessageBoxIcon.Information);else{string dir=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BikashRanjanNayak","BusinessFinance");Directory.CreateDirectory(dir);File.AppendAllText(System.IO.Path.Combine(dir,"technical.log"),DateTime.UtcNow.ToString("O")+" "+ex+Environment.NewLine);MessageBox.Show("The operation could not be completed. No incomplete financial posting was saved. Details were written to technical.log in the application data folder.","Operation stopped",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    public static void Run(Action action){try{action();}catch(Exception ex){Error(ex);}}
    public static Button Button(string text,Action action,int width=125){var b=new Button{Text=text,AutoSize=false,Width=width,Height=32,FlatStyle=FlatStyle.Flat,BackColor=Color.White,Margin=new Padding(3)};b.FlatAppearance.BorderColor=Color.FromArgb(208,218,226);b.Click+=(_,_)=>Run(action);return b;}
    public static DateTimePicker Date(DateTime? value=null)=>new(){Format=DateTimePickerFormat.Custom,CustomFormat="dd-MM-yyyy",Value=value??DateTime.Today,Width=125};
    public static int Day(DateTimePicker picker)=>DateOnly.FromDateTime(picker.Value).DayNumber;
    public static DataGridView Grid(){var grid=new DataGridView(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,ColumnHeadersHeight=36,RowTemplate={Height=30},EnableHeadersVisualStyles=false,ColumnHeadersDefaultCellStyle=new(){BackColor=Color.FromArgb(228,235,241),ForeColor=Navy},AlternatingRowsDefaultCellStyle=new(){BackColor=Color.FromArgb(248,250,252)}};grid.DataError+=(_,e)=>{e.ThrowException=false;e.Cancel=true;MessageBox.Show("Enter a valid value for this column. Amounts and quantities must be numbers.","Check this entry");};return grid;}
}
internal sealed class Login:Form
{
    public Login(Engine engine){bool setup=engine.NeedsSetup;Text=setup?"Create your administrator account":"Business Finance — Sign in";Width=470;Height=410;StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;Font=new("Segoe UI",10);BackColor=Ui.Back;
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(28),FlowDirection=FlowDirection.TopDown,WrapContents=false};panel.Controls.Add(new Label{Text="BUSINESS FINANCE",Font=new("Segoe UI",20,FontStyle.Bold),ForeColor=Ui.Navy,AutoSize=true});panel.Controls.Add(new Label{Text=setup?"Create a local account. No default password.":"Your business records stay on this computer.",AutoSize=true,Margin=new Padding(0,10,0,10)});
        var user=new TextBox{Width=360,PlaceholderText="Administrator name"};var pass=new TextBox{Width=360,PlaceholderText="Password (at least 12 characters)",UseSystemPasswordChar=true};var confirm=new TextBox{Width=360,PlaceholderText="Confirm password",UseSystemPasswordChar=true};panel.Controls.Add(user);panel.Controls.Add(pass);if(setup)panel.Controls.Add(confirm);
        var enter=Ui.Button(setup?"Create account":"Sign in",()=>{if(setup){Value.Require(pass.Text==confirm.Text,"Passwords do not match.");engine.Setup(user.Text,pass.Text);}else engine.Login(user.Text,pass.Text);DialogResult=DialogResult.OK;},360);enter.BackColor=Ui.Teal;enter.ForeColor=Color.White;panel.Controls.Add(enter);AcceptButton=enter;panel.Controls.Add(new Label{Text="Keep your password safely. This release has no password recovery bypass.",MaximumSize=new Size(365,0),AutoSize=true,Margin=new Padding(0,15,0,0)});Controls.Add(panel);Controls.Add(new Label{Dock=DockStyle.Bottom,Height=32,Text=Ui.Credit,TextAlign=ContentAlignment.MiddleCenter,Font=new("Segoe UI",8)});
    }
}
public sealed record Choice(long Id,string Name){public override string ToString()=>Name;}
internal sealed class Entry:Form
{
    readonly TableLayoutPanel panel=new(){Dock=DockStyle.Fill,AutoScroll=true,ColumnCount=2,Padding=new Padding(16)};
    public Dictionary<string,Control> Fields {get;}=[];
    readonly Dictionary<string,object?> initial;
    public Entry(string title,Dictionary<string,object?>? values=null){Text=title;Width=650;Height=650;StartPosition=FormStartPosition.CenterParent;Font=new("Segoe UI",10);BackColor=Ui.Back;initial=values??[];panel.ColumnStyles.Add(new(SizeType.Absolute,220));panel.ColumnStyles.Add(new(SizeType.Percent,100));Controls.Add(panel);}
    public void Add(string key,string label,string kind="text",IEnumerable<Choice>? choices=null,IEnumerable<string>? options=null,object? value=null,bool optional=false)
    {
        if(initial.TryGetValue(key,out var init))value=init is DBNull?null:init;Control c;
        if(choices!=null){var box=new ComboBox{DropDownStyle=ComboBoxStyle.DropDown,AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.ListItems};if(optional)box.Items.Add(new Choice(0,"— None —"));foreach(var ch in choices)box.Items.Add(ch);box.SelectedIndex=box.Items.Count>0?0:-1;if(value!=null)for(int i=0;i<box.Items.Count;i++)if(box.Items[i] is Choice item && item.Id==Value.Long(value))box.SelectedIndex=i;c=box;}
        else if(options!=null){var box=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};box.Items.AddRange(options.Cast<object>().ToArray());box.SelectedIndex=0;if(value!=null)box.SelectedItem=Value.Text(value);c=box;}
        else if(kind=="date"){var p=Ui.Date(value==null?null:DateOnly.FromDayNumber((int)Value.Long(value)).ToDateTime(TimeOnly.MinValue));p.ShowCheckBox=optional;p.Checked=!optional||value!=null;c=p;}
        else if(kind=="bool")c=new CheckBox{Checked=Value.Long(value)==1,Text="Yes"};
        else if(kind is "money" or "number" or "percent"){c=new NumericUpDown{DecimalPlaces=kind=="number"?0:2,Minimum=0,Maximum=kind=="percent"?100:999999999999,ThousandsSeparator=true,Value=value==null?0:kind=="money"?Value.Rupees(Value.Long(value)):Value.Dec(value)};}
        else c=new TextBox{Text=Value.Text(value),Multiline=kind=="multiline",Height=kind=="multiline"?70:28,UseSystemPasswordChar=kind=="password"};
        c.Dock=DockStyle.Top;c.Margin=new Padding(3,5,3,10);c.Tag=kind;Fields[key]=c;int row=panel.RowCount++;panel.RowStyles.Add(new(SizeType.AutoSize));panel.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(3,9,3,3)},0,row);panel.Controls.Add(c,1,row);
    }
    public object? Get(string key){var c=Fields[key];if(c is DateTimePicker d)return d.ShowCheckBox&&!d.Checked?null:Ui.Day(d);if(c is CheckBox b)return b.Checked?1:0;if(c is NumericUpDown n)return (string?)n.Tag=="money"?Value.Money(n.Value):n.Value;if(c is ComboBox cb&&cb.Items.Count>0&&cb.Items[0] is Choice){Value.Require(cb.SelectedItem is Choice,"Choose a listed record.");var ch=(Choice)cb.SelectedItem!;return ch.Id==0?null:ch.Id;}return c.Text.Trim();}
    public string S(string key)=>Value.Text(Get(key));public long N(string key)=>Value.Long(Get(key));public int D(string key)=>(int)N(key);public decimal Dec(string key)=>Value.Dec(Get(key));public long? Id(string key)=>Get(key) is null?null:N(key);
    public Dictionary<string,object?> Values()=>Fields.Keys.ToDictionary(k=>k,Get);
    public void Finish(Action<Entry> save,string text="Save"){var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};bar.Controls.Add(Ui.Button("Cancel",()=>DialogResult=DialogResult.Cancel));var button=Ui.Button(text,()=>{save(this);DialogResult=DialogResult.OK;},160);button.BackColor=Ui.Teal;button.ForeColor=Color.White;bar.Controls.Add(button);Controls.Add(bar);AcceptButton=button;}
}
