// Source-linked menu integration regression checks using recording stubs.
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using MossHarbor.ProductionProgress.Menus;
using MossHarbor.ProductionProgress.Core;

class Helper:IModHelper { public InputHelper Input {get;}=new(); public TranslationHelper Translation {get;}=new(); }
class Monitor:IMonitor { public static readonly List<string> Messages=new(); public void Log(string message,LogLevel level)=>Messages.Add(message); }
class Program {
 static int assertions;
 static void Check(bool condition,string message) { if(!condition)throw new Exception(message); assertions++; }
 static (MenuIntegration,GameMenu,Helper) Setup() {
  Game1.options=new(); Game1.input=new(); Game1.oldPadState=new(); GameMenu.forcePreventClose=false; Monitor.Messages.Clear();
  var menu=new GameMenu(); menu.pages.AddRange(new IClickableMenu[]{new(),new(),new CollectionsPage()});
  string[] names={"skills","social","collections"};
  for(int i=0;i<3;i++)menu.tabs.Add(new(new Rectangle(128+64*i,16,64,64),names[i],names[i]){myID=i+1,leftNeighborID=i,rightNeighborID=i+2});
  Game1.activeClickableMenu=menu;var helper=new Helper();var integration=new MenuIntegration(helper,()=>Array.Empty<ProgressRow>(),()=>0,()=>null,()=>{},new Monitor());integration.Attach(menu);return(integration,menu,helper);
 }
 static ButtonPressedEventArgs Click(SButton button=SButton.MouseLeft)=>new(){Button=button,Cursor=new(){ScreenPixels=new Vector2(216,40)}};
 static void Main(string[] args) {
  if(args.Contains("ultra-repro")){UltraMouseRepro();Console.WriteLine("PASS: cold proxy mouse-entry reproduction");return;}
  var (entry,menu,helper)=Setup();var batch=new SpriteBatch();menu.hoverText="Social";
  entry.BeforeDraw(batch);Check(batch.Events.SequenceEqual(new[]{"tab","tab"}),"background pass draws frame and icon only");Check(menu.hoverText=="","defer native tooltip");batch.Events.Add("native");entry.AfterDraw(batch);
  Check(batch.Events.SequenceEqual(new[]{"tab","tab","native","tab","tab","tooltip:Social","mouse"}),"normal draw order");Check(menu.hoverText=="Social","restore tooltip");
  batch=new();helper.Input.Cursor.ScreenPixels=new(216,40);entry.BeforeDraw(batch);entry.AfterDraw(batch);Check(batch.Events[^2]=="tooltip:Progress"&&batch.Events[^1]=="mouse","own tooltip below cursor");
  menu.currentTab=2;var collections=(CollectionsPage)menu.pages[2];collections.letterviewerSubMenu=new();entry.UpdateNavigation();batch=new();menu.hoverText="native-modal";
  entry.BeforeDraw(batch);Check(batch.Events.Count==2&&menu.hoverText=="native-modal","letter retains background icon without touching native hover");batch.Events.Add("letter");entry.AfterDraw(batch);Check(batch.Events.SequenceEqual(new[]{"tab","tab","letter"}),"no icon, tooltip or mouse above letter");
  entry.Input(Click());Check(ReferenceEquals(Game1.activeClickableMenu,menu)&&!helper.Input.Suppressed,"letter blocks mouse click-through");
  Game1.options.SnappyMenus=true;collections.currentlySnappedComponent=collections.allClickableComponents!.Single(c=>c.name=="MossHarbor.ProductionProgress");entry.Input(Click(SButton.ControllerA));Check(ReferenceEquals(Game1.activeClickableMenu,menu)&&!helper.Input.Suppressed,"letter blocks controller click-through");
  collections.letterviewerSubMenu=null;batch=new();entry.BeforeDraw(batch);entry.AfterDraw(batch);Check(batch.Events.Count(e=>e=="tab")==4,"closing letter restores foreground pass");
  foreach(bool pageChild in new[]{false,true}) {
   if(pageChild)collections.Child=new();else menu.Child=new();batch=new();entry.BeforeDraw(batch);entry.AfterDraw(batch);Check(batch.Events.SequenceEqual(new[]{"tab","tab"}),"child menus retain only background icon");entry.Input(Click());Check(ReferenceEquals(Game1.activeClickableMenu,menu),"child blocks click-through");collections.Child=null;menu.Child=null;
  }
  menu.invisible=true;batch=new();entry.BeforeDraw(batch);entry.AfterDraw(batch);Check(batch.Events.Count==0,"native invisible menu has no tabs");menu.invisible=false;
  menu.hoverText="new-game-value";entry.BeforeDraw(new());menu.hoverText="other-mod-value";entry.AfterDraw(new());Check(menu.hoverText=="other-mod-value","preserve newer hover value");
  menu.hoverText="interrupted";entry.BeforeDraw(new());entry.Detach();Check(menu.hoverText=="interrupted","detach restores deferred tooltip");Check(menu.pages.Count==3&&menu.tabs[1].bounds.X==192,"detach restores owned layout and page");entry.Attach(menu);
  menu.hoverText="next-frame";entry.BeforeDraw(new());entry.BeforeDraw(new());entry.AfterDraw(new());Check(menu.hoverText=="next-frame","next draw recovers omitted after-event");
  Game1.options.SnappyMenus=false;entry.Input(Click());Check(Game1.activeClickableMenu is ProductionMenu&&helper.Input.Suppressed,"entry remains clickable after letter close and reattach");batch=new();entry.BeforeDraw(batch);entry.AfterDraw(batch);Check(batch.Rectangles[0].Y==24&&batch.Rectangles[2].Y==24,"selected offset matches in both passes");
  Game1.activeClickableMenu!.Child=new();batch=new();entry.BeforeDraw(batch);entry.AfterDraw(batch);Check(batch.Events.SequenceEqual(new[]{"tab","tab"}),"production wrapper child retains background only");
  entry.Detach();var (replacement,newMenu,newHelper)=Setup();batch=new();replacement.BeforeDraw(batch);replacement.AfterDraw(batch);Check(batch.Events.Count(e=>e=="tab")==4,"replacement menu draws after reopen or resize");replacement.Detach();
  Check(Monitor.Messages.Count==0,"normal render regression has no detachment log");
  Diagnostics();
  UltraCompatibility();
  SpaceCoreCompatibility();
  Console.WriteLine($"PASS: {assertions} assertions against the actual MenuIntegration.cs and ProductionMenu.cs with recording UI stubs; no game assemblies loaded.");
 }
 static void Diagnostics() {
  void Run(string reason,Action<GameMenu> mutate,string detail) {
   var (entry,menu,helper)=Setup();mutate(menu);entry.UpdateNavigation();
   Check(Monitor.Messages.Count==1,"exactly one diagnostic: "+reason);
   string log=Monitor.Messages[0];Check(log.Contains("Reason="+reason+";"),"precise reason: "+log);
   Check(log.Contains(detail),"specific failure detail: "+detail);
   Check(log.Contains("Expected pages:")&&log.Contains("Tabs (")&&log.Contains("Pages ("),"snapshot included");
   entry.UpdateNavigation();entry.BeforeDraw(new());entry.AfterDraw(new());entry.Input(Click());
   Check(Monitor.Messages.Count==1,"detached menu does not repeatedly log");
   Check(ReferenceEquals(Game1.activeClickableMenu,menu),"unselected native menu remains active");
  }
  Run("progress-page-missing",m=>m.pages.RemoveAt(3),"no longer in GameMenu.pages");
  Run("anchor-tab-missing",m=>m.tabs.RemoveAt(0),"anchor=skills");
  Run("anchor-tab-missing",m=>m.tabs.RemoveAt(1),"anchor=social");
  Run("anchor-page-changed",m=>m.pages[0]=new IClickableMenu(),"anchor=before");
  Run("anchor-page-changed",m=>m.pages[1]=new IClickableMenu(),"anchor=social");
  Run("anchor-mapping-invalid",m=>m.Mappings["social"]=-1,"mappedIndex=-1");
  Run("anchor-mapping-invalid",m=>m.Mappings["social"]=999,"actual=null");
  Run("tab-overlap",m=>m.tabs[1].bounds=new(192,16,64,64),"last-applied=");
  // An extra preceding tab has a distinct identity, so test its removal separately.
  var (unused,baseMenu,unusedHelper)=Setup();unused.Detach();
  baseMenu.tabs[1].bounds=new(256,16,64,64);baseMenu.tabs[2].bounds=new(320,16,64,64);
  var custom=new ClickableComponent(new(192,16,64,64),"custom","custom"){myID=9};baseMenu.tabs.Insert(1,custom);baseMenu.pages.Add(new());baseMenu.Mappings["custom"]=3;
  var h=new Helper();var customEntry=new MenuIntegration(h,()=>Array.Empty<ProgressRow>(),()=>0,()=>null,()=>{},new Monitor());customEntry.Attach(baseMenu);baseMenu.tabs.Remove(custom);customEntry.UpdateNavigation();
  Check(Monitor.Messages.Single().Contains("Reason=anchor-tab-missing; anchor=before"),"distinct before-tab removal");
  // Same concrete type replacements must still be distinguishable by instance id.
  var (sameEntry,sameMenu,sameHelper)=Setup();var old=sameMenu.pages[1];sameMenu.pages[1]=new IClickableMenu();sameEntry.UpdateNavigation();
  Check(MenuIntegration.DescribePage(old)!=MenuIntegration.DescribePage(sameMenu.pages[1]),"same-type page identities distinguishable");
  Check(Monitor.Messages[0].Contains("expected="+MenuIntegration.DescribePage(old)),"expected identity logged");
  // Failure snapshot is taken before our layout restoration.
  var (overlap,overlapMenu,overlapHelper)=Setup();overlapMenu.tabs[1].bounds=new(192,16,64,64);overlap.UpdateNavigation();
  Check(Monitor.Messages[0].Contains("last-applied="),"capture altered bounds before detach");
  Check(overlapMenu.tabs[2].bounds.X==256,"unchanged owned bounds restored");
  void Forward(string reason,Action<GameMenu> prepare) {
   var (entry,menu,helper)=Setup();entry.Input(Click());var root=(ProductionMenu)Game1.activeClickableMenu!;prepare(menu);root.draw(new());
   Check(Monitor.Messages.Count==1&&Monitor.Messages[0].Contains("Reason="+reason+";"),"forward failure reason");
   Check(Monitor.Messages[0].Contains("operation=draw"),"forward call identified");
   Check(Game1.activeClickableMenu==null,"selected broken menu exits");entry.UpdateNavigation();Check(Monitor.Messages.Count==1,"no repeated forward log");
  }
  Forward("forward-progress-page-missing",m=>m.pages.RemoveAt(3));
  Forward("forward-current-page-invalid",m=>m.currentTab=-1);
  Forward("forward-original-page-lost",m=>m.ForwardAction=()=>m.pages.RemoveAt(0));
  var (invalidSelected,selectedMenu,selectedHelper)=Setup();invalidSelected.Input(Click());selectedMenu.pages[1]=new();invalidSelected.UpdateNavigation();
  Check(Game1.activeClickableMenu==null,"validation failure still closes selected wrapper");
  // Snapshot stays bounded even when many pages/tabs are registered.
  var (bounded,boundedMenu,boundedHelper)=Setup();for(int i=0;i<60;i++){boundedMenu.pages.Add(new());boundedMenu.tabs.Add(new(new(1000+i*64,16,64,64),"extra"+i,""));}boundedMenu.pages.RemoveAt(3);bounded.UpdateNavigation();
  Check(!Monitor.Messages[0].Contains("extra59")&&Monitor.Messages[0].Contains("first 24 at most"),"bounded failure snapshot");
 }

 static Type MakeProxyType(string assembly) {
  var a=System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName(assembly){Version=new Version(2,5,0,0)},System.Reflection.Emit.AssemblyBuilderAccess.Run);
  var t=a.DefineDynamicModule(assembly).DefineType("UltraSmooth.Optimizers.SmoothTabProxy",System.Reflection.TypeAttributes.Public,typeof(IClickableMenu));t.DefineDefaultConstructor(System.Reflection.MethodAttributes.Public);return t.CreateType()!;
 }
 static readonly Type ProxyType=MakeProxyType("UltraSmooth"),ImpostorProxyType=MakeProxyType("DifferentMod");
 static IClickableMenu Proxy(Type? type=null)=>(IClickableMenu)Activator.CreateInstance(type??ProxyType)!;
 static (MenuIntegration,GameMenu,Helper) UltraSetup(int current=0,bool lazy=true) {
  var (entry,menu,helper)=Setup();entry.Detach();
  menu.pages[0]=lazy&&current!=0?Proxy():new SkillsPage();menu.pages[1]=lazy&&current!=1?Proxy():new SocialPage();menu.currentTab=current;
  menu.BeforeChangeTab=(m,i)=>{if(lazy&&m.pages[i].GetType()==ProxyType)m.pages[i]=i==0?new SkillsPage():new SocialPage();};
  entry.Attach(menu);return(entry,menu,helper);
 }
 static void UltraMouseRepro() {
  var (entry,menu,helper)=UltraSetup(1);entry.Input(Click());entry.UpdateNavigation();
  Check(Game1.activeClickableMenu is ProductionMenu,"cold skills proxy replacement must still open Progress; log="+string.Join(" | ",Monitor.Messages));entry.Detach();
 }
 static void UltraCompatibility() {
  UltraMouseRepro();
  bool HasWarning()=>Monitor.Messages.Any(s=>s.Contains("Reason="));
  foreach(bool lazy in new[]{false,true}) {
   var (entry,menu,helper)=UltraSetup(0,lazy);Game1.options.gamepadControls=true;
   Game1.input.State=new(false,true);entry.BeforeUpdate();menu.changeTab(1,false);entry.UpdateNavigation(tick:true);
   Check(Game1.activeClickableMenu is ProductionMenu,"right trigger enters Progress, lazy="+lazy);
   Check(!HasWarning(),"native or materialized target accepted");
   ((ProductionMenu)Game1.activeClickableMenu!).receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons.RightTrigger);
   Check(ReferenceEquals(Game1.activeClickableMenu,menu)&&menu.currentTab==1,"exit to Social, lazy="+lazy);
   Game1.oldPadState=new(false,true);Game1.input.State=new(false,true);entry.BeforeUpdate();entry.UpdateNavigation(tick:true);
   Check(ReferenceEquals(Game1.activeClickableMenu,menu),"held trigger does not reenter");
   Game1.oldPadState=new();Game1.input.State=new(true,false);entry.BeforeUpdate();menu.changeTab(0,false);entry.UpdateNavigation(tick:true);
   Check(Game1.activeClickableMenu is ProductionMenu,"left trigger enters Progress, lazy="+lazy);
   ((ProductionMenu)Game1.activeClickableMenu!).receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons.LeftTrigger);
   Check(ReferenceEquals(Game1.activeClickableMenu,menu)&&menu.currentTab==0,"exit to Skills");
   Check(!HasWarning(),"bidirectional transitions preserve mapping");
   if(!lazy)Check(Monitor.Messages.Count==0,"absent/disabled lazy mod never enters compatibility path");
   else Check(Monitor.Messages.Count==0,"normal compatibility refresh remains silent");
   entry.Detach();
  }
  var (coldLeft,coldLeftMenu,coldLeftHelper)=UltraSetup(1);Game1.options.gamepadControls=true;Game1.input.State=new(true,false);coldLeft.BeforeUpdate();coldLeftMenu.changeTab(0,false);coldLeft.UpdateNavigation(tick:true);
  Check(Game1.activeClickableMenu is ProductionMenu&&!HasWarning(),"cold left trigger survives Skills materialization");coldLeft.Detach();
  // Both anchors initially lazy: direct click must materialize Skills, then exiting
  // Progress must materialize Social without losing our entry or using the old object.
  var (both,bothMenu,bothHelper)=UltraSetup(2);both.Input(Click());Check(Game1.activeClickableMenu is ProductionMenu&&bothMenu.pages[0] is SkillsPage,"cold entry with both anchors lazy");
  ((ProductionMenu)Game1.activeClickableMenu!).receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons.RightTrigger);
  Check(ReferenceEquals(Game1.activeClickableMenu,bothMenu)&&bothMenu.pages[1] is SocialPage,"cold Social exit materializes and rebinds");
  Check(bothMenu.pages[1].allClickableComponents!.Any(c=>c.name=="MossHarbor.ProductionProgress"),"new active page receives navigation entry");
  Check(!HasWarning(),"cold entry/exit has no warning");both.Detach();
  // Native mouse tab switch is another entry point for materialization.
  var (native,nativeMenu,nativeHelper)=UltraSetup();nativeMenu.changeTab(1,false);native.UpdateNavigation();Check(!HasWarning()&&nativeMenu.pages.Count==4,"native mouse tab materialization retains Progress");native.Detach();
  void Reject(Action<GameMenu> mutate,string expectedReason) {
   var (entry,menu,helper)=UltraSetup();mutate(menu);entry.UpdateNavigation();
   Check(Monitor.Messages.Count(s=>s.Contains("Reason="))==1&&Monitor.Messages.Any(s=>s.Contains("Reason="+expectedReason+";")),"invalid transition must reject: "+expectedReason);
   Check(!Monitor.Messages.Any(s=>s.Contains("Accepted Ultra Smooth")),"no partial commit before all validations pass");entry.Detach();
  }
  Reject(m=>m.pages[1]=new SkillsPage(),"anchor-page-changed");
  Reject(m=>m.pages[1]=new IClickableMenu(),"anchor-page-changed");
  Reject(m=>{var old=m.pages[1];m.pages[1]=new SocialPage();m.pages.Add(old);},"anchor-page-changed");
  Reject(m=>{var actual=new SocialPage();m.pages[1]=actual;m.pages.Add(actual);},"anchor-page-changed");
  Reject(m=>{m.pages[1]=new SocialPage();m.tabs.Add(new(new(900,16,64,64),"social",""));},"anchor-page-changed");
  Reject(m=>{m.pages[1]=new SocialPage();m.Mappings["social"]=2;},"anchor-page-changed");
  Reject(m=>m.Mappings["social"]=-1,"anchor-mapping-invalid");
  Reject(m=>{m.pages[1]=new SocialPage();m.tabs[1].bounds=new(192,16,64,64);},"tab-overlap");
  // An identical class name from another assembly isn't the inspected proxy.
  var (wrong,wrongMenu,wrongHelper)=UltraSetup();wrong.Detach();wrongMenu.pages[1]=Proxy(ImpostorProxyType);wrong.Attach(wrongMenu);wrongMenu.pages[1]=new SocialPage();wrong.UpdateNavigation();Check(HasWarning(),"wrong assembly keeps protection");wrong.Detach();
  // Resize while Progress is selected: restore through the existing two-tick path.
  var (resizing,resizingMenu,resizingHelper)=UltraSetup(1);resizing.Input(Click());((ProductionMenu)Game1.activeClickableMenu!).gameWindowSizeChanged(new(),new());
  var (discard,resizedMenu,resizedHelper)=UltraSetup();discard.Detach();resizing.MenuChanged(resizedMenu);resizing.UpdateNavigation(tick:true);resizing.UpdateNavigation(tick:true);
  Check(Game1.activeClickableMenu is ProductionMenu&&!HasWarning(),"resize resumes Progress with fresh proxy anchors");resizing.Detach();
  // A new menu gets fresh indices and proxy references instead of reusing old state.
  var (oldEntry,oldMenu,oldHelper)=UltraSetup();var (unused,replacement,replacementHelper)=UltraSetup(1);unused.Detach();oldEntry.MenuChanged(replacement);oldEntry.Input(Click());Check(Game1.activeClickableMenu is ProductionMenu,"replacement menu opens after reattach");Check(!HasWarning(),"replacement menu has no stale anchor failure");oldEntry.Detach();
 }

 // Reproduce the observed Ultra Smooth -> SpaceCore transition with an independent
 // IClickableMenu subclass. Inheriting SkillsPage here would conceal the real bug.
 static Type MakeSkillsType(string assemblyName, string fullName = "SpaceCore.Interface.NewSkillsPage") {
  var a=System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName(assemblyName){Version=new Version(1,28,4,0)},System.Reflection.Emit.AssemblyBuilderAccess.Run);
  var t=a.DefineDynamicModule(assemblyName).DefineType(fullName,System.Reflection.TypeAttributes.Public,typeof(IClickableMenu));
  t.DefineDefaultConstructor(System.Reflection.MethodAttributes.Public);return t.CreateType()!;
 }
 static readonly Type SpaceSkills=MakeSkillsType("SpaceCore"), WrongSkillsAssembly=MakeSkillsType("AnotherMod"), WrongSkillsClass=MakeSkillsType("SpaceCore","SpaceCore.Interface.OtherPage");
 static IClickableMenu NewSpaceSkills(Type? type=null)=>(IClickableMenu)Activator.CreateInstance(type??SpaceSkills)!;
 static (MenuIntegration,GameMenu,Helper) SpaceSetup(int current, bool lazy=true) {
  var (entry,menu,helper)=UltraSetup(current,lazy);entry.Detach();
  if(!lazy || current==0)menu.pages[0]=NewSpaceSkills();
  menu.BeforeChangeTab=(m,i)=>{if(lazy&&m.pages[i].GetType()==ProxyType)m.pages[i]=i==0?NewSpaceSkills():new SocialPage();};
  entry.Attach(menu);return(entry,menu,helper);
 }
 static void SpaceCoreCompatibility() {
  foreach(bool lazy in new[]{false,true}) foreach(int current in new[]{0,1,2}) {
   var (e,m,h)=SpaceSetup(current,lazy);e.Input(Click());e.UpdateNavigation();
   Check(Game1.activeClickableMenu is ProductionMenu,"SpaceCore mouse entry; lazy="+lazy+", current="+current+": "+string.Join(" | ",Monitor.Messages));
   Check(m.pages[0].GetType()==SpaceSkills,"skill page remains SpaceCore's instance");
   ((ProductionMenu)Game1.activeClickableMenu!).receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons.LeftTrigger);
   Check(ReferenceEquals(Game1.activeClickableMenu,m)&&m.currentTab==0,"exit to actual custom Skills page");
   Check(m.pages[0].allClickableComponents!.Any(c=>c.name=="MossHarbor.ProductionProgress"),"custom Skills receives navigation entry");
   Game1.options.gamepadControls=true;Game1.input.State=new(false,true);Game1.oldPadState=new();e.BeforeUpdate();m.changeTab(1,false);e.UpdateNavigation(tick:true);
   Check(Game1.activeClickableMenu is ProductionMenu,"RT from SpaceCore Skills enters Progress");
   ((ProductionMenu)Game1.activeClickableMenu!).receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons.RightTrigger);
   Check(ReferenceEquals(Game1.activeClickableMenu,m)&&m.currentTab==1,"exit to Social");
   Game1.input.State=new(true,false);Game1.oldPadState=new();e.BeforeUpdate();m.changeTab(0,false);e.UpdateNavigation(tick:true);
   Check(Game1.activeClickableMenu is ProductionMenu,"LT from Social enters Progress");
   Check(Monitor.Messages.Count==0,"valid combination is silent");e.Detach();
   Check(m.pages.Count==3&&!m.pages.Any(p=>p.allClickableComponents!.Any(c=>c.name=="MossHarbor.ProductionProgress")),"detach removes only owned entry");
   Game1.activeClickableMenu=m;e.Attach(m);h.Input.Suppressed=false;e.Input(Click());
   Check(Game1.activeClickableMenu is ProductionMenu,"reopen with materialized custom Skills");e.Detach();
  }
  // The report had Skills in slot 1, alongside extra unrelated page replacements.
  var (entry,menu,helper)=SpaceSetup(2);entry.Detach();
  menu.pages.Insert(0,new());menu.tabs.Insert(0,new(new(64,16,64,64),"inventory",""){myID=20});
  menu.Mappings["inventory"]=0;menu.Mappings["skills"]=1;menu.Mappings["social"]=2;menu.Mappings["collections"]=3;menu.currentTab=0;
  menu.pages.Add(new());menu.pages.Add(new());
  menu.BeforeChangeTab=(m,i)=>{int page=m.getTabNumberFromName(m.tabs[i].name);if(m.pages[page].GetType()==ProxyType)m.pages[page]=page==1?NewSpaceSkills():new SocialPage();};
  entry.Attach(menu);entry.Input(Click());Check(Game1.activeClickableMenu is ProductionMenu&&menu.pages[1].GetType()==SpaceSkills,"reported slot 1 and extra pages");
  ((ProductionMenu)Game1.activeClickableMenu!).gameWindowSizeChanged(new(),new());
  var (discard,replacement,unused)=SpaceSetup(2);discard.Detach();entry.MenuChanged(replacement);entry.UpdateNavigation(tick:true);entry.UpdateNavigation(tick:true);
  Check(Game1.activeClickableMenu is ProductionMenu&&Monitor.Messages.Count==0,"resize resumes with fresh custom Skills");entry.Detach();
  // UI Info Suite removes its appended options page when the root changes and
  // appends a new one on return. Our own page index must follow that removal.
  var (options,optionsMenu,optionsHelper)=SpaceSetup(2);
  var externalOptions=new IClickableMenu();optionsMenu.pages.Insert(3,externalOptions);
  options.Input(Click());optionsMenu.pages.Remove(externalOptions);options.UpdateNavigation();
  Game1.activeClickableMenu!.update(new());
  Check(Game1.activeClickableMenu is ProductionMenu&&Monitor.Messages.Count==0,"external options removal before our page keeps forwarding valid");
  ((ProductionMenu)Game1.activeClickableMenu!).receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons.LeftTrigger);
  optionsMenu.pages[0]=NewSpaceSkills();optionsMenu.pages.Add(new());options.MenuChanged(optionsMenu);options.UpdateNavigation();
  optionsHelper.Input.Suppressed=false;options.Input(Click());
  Check(Game1.activeClickableMenu is ProductionMenu&&Monitor.Messages.Count==0,"options reinjection and SpaceCore rebuild coexist");
  // Replacements outside the Skills/Social anchors don't need type exceptions.
  optionsMenu.pages[2]=new();options.UpdateNavigation();
  Check(Game1.activeClickableMenu is ProductionMenu&&Monitor.Messages.Count==0,"unrelated page replacement retains entry");options.Detach();
  void Reject(Action<GameMenu> mutate,string reason="anchor-page-changed") {
   var (e,m,h)=SpaceSetup(2);mutate(m);e.UpdateNavigation();
   Check(Monitor.Messages.Count==1&&Monitor.Messages[0].Contains("Reason="+reason+";"),"invalid SpaceCore transition rejects: "+reason);e.Detach();
  }
  Reject(m=>m.pages[0]=NewSpaceSkills(WrongSkillsAssembly));
  Reject(m=>m.pages[0]=NewSpaceSkills(WrongSkillsClass));
  Reject(m=>{var old=m.pages[0];m.pages[0]=NewSpaceSkills();m.pages.Add(old);});
  Reject(m=>{var actual=NewSpaceSkills();m.pages[0]=actual;m.pages.Add(actual);});
  Reject(m=>{m.pages[0]=NewSpaceSkills();m.tabs.Add(new(new(900,16,64,64),"skills",""));});
  Reject(m=>{m.pages[0]=NewSpaceSkills();m.Mappings["skills"]=1;});
  Reject(m=>m.pages[1]=NewSpaceSkills());
  Reject(m=>{m.pages[0]=NewSpaceSkills();m.tabs[1].bounds=new(192,16,64,64);},"tab-overlap");
  Reject(m=>m.pages.RemoveAt(3),"progress-page-missing");
  // SpaceCore's MenuChanged handler rebuilds Skills when our wrapper returns
  // to the existing GameMenu. Exercise both subscriber orders.
  foreach(bool spaceCoreFirst in new[]{false,true}) {
   var (stable,m,h)=SpaceSetup(0,false);stable.Input(Click());
   ((ProductionMenu)Game1.activeClickableMenu!).receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons.LeftTrigger);
   if(spaceCoreFirst)m.pages[0]=NewSpaceSkills();
   stable.MenuChanged(m);
   if(!spaceCoreFirst)m.pages[0]=NewSpaceSkills();
   stable.UpdateNavigation();h.Input.Suppressed=false;stable.Input(Click());
   Check(Game1.activeClickableMenu is ProductionMenu&&Monitor.Messages.Count==0,"SpaceCore rebuild on return retains Progress, handler order="+spaceCoreFirst);
   stable.Detach();
  }
  var (initial,initialMenu,initialHelper)=UltraSetup(0,false);initialMenu.pages[0]=NewSpaceSkills();initial.UpdateNavigation();initial.Input(Click());
  Check(Game1.activeClickableMenu is ProductionMenu&&Monitor.Messages.Count==0,"SpaceCore initial replacement after attach");initial.Detach();
  var (unknown,unknownMenu,unknownHelper)=Setup();unknownMenu.pages[0]=NewSpaceSkills();unknown.UpdateNavigation();
  Check(Monitor.Messages.Count==1&&Monitor.Messages[0].Contains("anchor-page-changed"),"unknown previous skill page still rejected");unknown.Detach();
 }

}
