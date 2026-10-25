// Recording UI doubles; no game or third-party assemblies are loaded.
using MossHarbor.ProductionProgress.Core;
namespace Microsoft.Xna.Framework {
 public record struct Rectangle(int X,int Y,int Width,int Height) { public int Left=>X; public int Right=>X+Width; public int Top=>Y; public int Bottom=>Y+Height; public bool Contains(int x,int y)=>x>=Left&&x<Right&&y>=Top&&y<Bottom; public bool Intersects(Rectangle o)=>Left<o.Right&&Right>o.Left&&Top<o.Bottom&&Bottom>o.Top; }
 public record struct Vector2(float X,float Y);
 public class GameTime {}
 public struct Color { public static Color White=>new(); }
}
namespace Microsoft.Xna.Framework.Graphics {
 public class SpriteBatch { public readonly List<string> Events=new(); public readonly List<Microsoft.Xna.Framework.Rectangle> Rectangles=new(); public void Draw(object texture,Microsoft.Xna.Framework.Rectangle bounds,Microsoft.Xna.Framework.Rectangle? source,Microsoft.Xna.Framework.Color color) { Events.Add("tab"); Rectangles.Add(bounds); } }
}
namespace Microsoft.Xna.Framework.Input {
 public enum Buttons { LeftTrigger, RightTrigger, B, Back, Start }
 public enum Keys { Escape, A }
 public record struct GamePadState(bool Left=false,bool Right=false) { public bool IsButtonDown(Buttons button)=>button==Buttons.LeftTrigger?Left:button==Buttons.RightTrigger&&Right; public bool IsButtonUp(Buttons button)=>!IsButtonDown(button); }
}
namespace StardewModdingAPI {
 public enum SButton { MouseLeft, ControllerA, DPadDown, DPadUp, LeftTrigger, RightTrigger }
 public enum LogLevel { Warn, Debug, Info, Error }
 public static class Context { public static bool IsWorldReady=true; }
 public class ModRegistry { public bool Loaded; public object? Api; public Exception? Failure; public int Reads; public bool IsLoaded(string id)=>Loaded; public T? GetApi<T>(string id) where T:class { Reads++; if(Failure!=null)throw Failure;return Api as T; } public ModInfo? Get(string id)=>Loaded?new():null; }
 public class ModInfo { public Manifest Manifest {get;}=new(); }
 public class Manifest { public string Version=>"1.0.1"; }
 public interface IMonitor { void Log(string text,LogLevel level); }
 public class Cursor { public Microsoft.Xna.Framework.Vector2 ScreenPixels {get;set;} }
 public class InputHelper { public readonly HashSet<SButton> Suppressed=new(); public Cursor Cursor=new(); public bool IsSuppressed(SButton b)=>Suppressed.Contains(b); public void Suppress(SButton b)=>Suppressed.Add(b); public Cursor GetCursorPosition()=>Cursor; }
 public class TranslationHelper { public string Get(string key)=>"Progress"; }
 public interface IModHelper { InputHelper Input {get;} TranslationHelper Translation {get;} ModRegistry ModRegistry {get;} }
}
namespace StardewModdingAPI.Events {
 public enum EventPriority { Normal }
 public class ButtonPressedEventArgs { public StardewModdingAPI.SButton Button; public StardewModdingAPI.Cursor Cursor=new(); }
}
namespace StardewValley.ItemTypeDefinitions {
 public class ParsedItemData { public object GetTexture()=>new(); public Microsoft.Xna.Framework.Rectangle GetSourceRect(int i,object? o)=>new(0,0,16,16); }
}
namespace StardewValley {
 public static class ItemRegistry { public static ItemTypeDefinitions.ParsedItemData GetDataOrErrorItem(string id)=>new(); }
 public class Options { public bool SnappyMenus; public bool snappyMenus; public bool gamepadControls; public string gamepadMode="Auto"; public float uiScale=1,zoomLevel=1; public object menuButton=new(); public bool doesInputListContain(object list,Microsoft.Xna.Framework.Input.Keys key)=>false; }
 // Same transform as the inspected native Utility method; ScreenPixels has already divided out world zoom.
 public static class Utility { public static Microsoft.Xna.Framework.Vector2 ModifyCoordinatesForUIScale(Microsoft.Xna.Framework.Vector2 p)=>new(p.X/Game1.options.uiScale*Game1.options.zoomLevel,p.Y/Game1.options.uiScale*Game1.options.zoomLevel); }
 public class Input { public Microsoft.Xna.Framework.Input.GamePadState State; public Microsoft.Xna.Framework.Input.GamePadState GetGamePadState()=>State; }
 public static class Game1 { public static Menus.IClickableMenu? activeClickableMenu; public static object mouseCursors=new(),smallFont=new(); public static Options options=new(); public static Input input=new(); public static Microsoft.Xna.Framework.Input.GamePadState oldPadState; public static void playSound(string s){} public static void exitActiveMenu()=>activeClickableMenu=null; }
}
namespace StardewValley.Menus {
 using Microsoft.Xna.Framework;
 using Microsoft.Xna.Framework.Graphics;
 public class ClickableComponent { public Rectangle bounds; public string name; public int myID,leftNeighborID,rightNeighborID,downNeighborID; public bool tryDefaultIfNoDownNeighborExists,fullyImmutable; public bool containsPoint(int x,int y)=>bounds.Contains(x,y); public ClickableComponent(Rectangle b,string n,string label){bounds=b;name=n;} }
 public class IClickableMenu {
 public int xPositionOnScreen,yPositionOnScreen,width=880,height=680; public static int tabYPositionRelativeToMenuY=-48; public ClickableComponent? upperRightCloseButton;
 public IClickableMenu(){} public IClickableMenu(int x,int y,int w,int h){xPositionOnScreen=x;yPositionOnScreen=y;width=w;height=h;}
 public virtual void update(GameTime time){} public virtual void draw(SpriteBatch b){} public virtual void receiveLeftClick(int x,int y,bool playSound=true){}
 public virtual void receiveRightClick(int x,int y,bool playSound=true){} public virtual void receiveScrollWheelAction(int direction){}
 public virtual void leftClickHeld(int x,int y){} public virtual void releaseLeftClick(int x,int y){}
 public virtual void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys key){} public virtual void receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons button){}
 public virtual void performHoverAction(int x,int y){} public virtual void gameWindowSizeChanged(Rectangle oldBounds,Rectangle newBounds){}
 public virtual void snapToDefaultClickableComponent(){} public virtual void setCurrentlySnappedComponentTo(int id){} public virtual void applyMovementKey(int direction){}
 public virtual void automaticSnapBehavior(int direction,int oldRegion,int oldId){} public void exitThisMenu()=>StardewValley.Game1.exitActiveMenu(); public IClickableMenu? Child; public List<ClickableComponent>? allClickableComponents=new(); public ClickableComponent? currentlySnappedComponent; public IClickableMenu? GetChildMenu()=>Child; public bool CanClose=true; public virtual bool readyToClose()=>CanClose; public virtual ClickableComponent getCurrentlySnappedComponent()=>currentlySnappedComponent!; public virtual void snapCursorToCurrentSnappedComponent(){} public static void drawHoverText(SpriteBatch b,string text,object font)=>b.Events.Add("tooltip:"+text); }
 public class SkillsPage:IClickableMenu {} public class SocialPage:IClickableMenu {}
 public class CollectionsPage:IClickableMenu { public IClickableMenu? letterviewerSubMenu; }
 public class GameMenu:IClickableMenu {
 public Dictionary<string,int> Mappings=new(); public List<IClickableMenu> pages=new(); public List<ClickableComponent> tabs=new();
 public int currentTab,lastOpenedNonMapTab; public bool invisible; public static bool forcePreventClose; public static int mapTab=3;
 public string hoverText=""; public Action<GameMenu,int>? BeforeChangeTab; public int NativeChanges,CloseButtonInitializations;
 public int getTabNumberFromName(string s)=>Mappings.TryGetValue(s,out int i)?i:-1;
 public void changeTab(int i,bool sound) { if(forcePreventClose||!readyToClose())return; if(i<0||i>=tabs.Count)throw new Exception("Native tab index out of range");BeforeChangeTab?.Invoke(this,i);NativeChanges++;int next=getTabNumberFromName(tabs[i].name);if(next==mapTab)lastOpenedNonMapTab=currentTab;currentTab=next;invisible=next==mapTab; }
 public override bool readyToClose()=>pages[currentTab].readyToClose();
 public void initializeUpperRightCloseButton(){CloseButtonInitializations++;upperRightCloseButton=new(new(1000,0,64,64),"close","");}
 public override void draw(SpriteBatch b) {b.Events.Add("native-frame");b.Events.Add("native-tabs");pages[currentTab].draw(b);drawMouse(b,true);}
 public override void update(GameTime t)=>pages[currentTab].update(t);
 public override void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys k) {if(k==Microsoft.Xna.Framework.Input.Keys.Escape&&readyToClose())Game1.exitActiveMenu();else pages[currentTab].receiveKeyPress(k);}
 public override void receiveLeftClick(int x,int y,bool playSound=true){if(upperRightCloseButton?.containsPoint(x,y)==true&&readyToClose())Game1.exitActiveMenu();else pages[currentTab].receiveLeftClick(x,y,playSound);}
 public void AddTabsToClickableComponents(IClickableMenu page)=>page.allClickableComponents!.AddRange(tabs);
 public void drawMouse(SpriteBatch b,bool always)=>b.Events.Add("mouse");
 }

}
namespace MossHarbor.ProductionProgress.Menus {
 using StardewValley.Menus;
 public class ProgressPage:IClickableMenu {
 private readonly Action opened; public int Opens,Draws,Updates;public bool Released;
 public ProgressPage(IClickableMenu m,StardewModdingAPI.TranslationHelper t,Func<IReadOnlyCollection<ProgressRow>> rows,Func<long> rev,Func<string?> status,Action opened,Func<int> tabsBottom){this.opened=opened;}
 public void ReleaseResources()=>Released=true;public void Open(){Opens++;opened();}public void Scroll(int n){}public void populateClickableComponentList(){}
 public override void draw(Microsoft.Xna.Framework.Graphics.SpriteBatch b){Draws++;b.Events.Add("progress-content");}
 public override void update(Microsoft.Xna.Framework.GameTime t)=>Updates++;
 }


}
