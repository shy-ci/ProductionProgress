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
 public enum SButton { MouseLeft, ControllerA, DPadDown, DPadUp }
 public enum LogLevel { Warn, Debug }
 public interface IMonitor { void Log(string text,LogLevel level); }
 public class Cursor { public Microsoft.Xna.Framework.Vector2 ScreenPixels {get;set;} }
 public class InputHelper { public bool Suppressed; public Cursor Cursor=new(); public bool IsSuppressed(SButton b)=>Suppressed; public void Suppress(SButton b)=>Suppressed=true; public Cursor GetCursorPosition()=>Cursor; }
 public class TranslationHelper { public string Get(string key)=>"Progress"; }
 public interface IModHelper { InputHelper Input {get;} TranslationHelper Translation {get;} }
}
namespace StardewModdingAPI.Events {
 public class ButtonPressedEventArgs { public StardewModdingAPI.SButton Button; public StardewModdingAPI.Cursor Cursor=new(); }
}
namespace StardewValley.ItemTypeDefinitions {
 public class ParsedItemData { public object GetTexture()=>new(); public Microsoft.Xna.Framework.Rectangle GetSourceRect(int i,object? o)=>new(0,0,16,16); }
}
namespace StardewValley {
 public static class ItemRegistry { public static ItemTypeDefinitions.ParsedItemData GetDataOrErrorItem(string id)=>new(); }
 public class Options { public bool SnappyMenus; public bool gamepadControls; public object menuButton=new(); public bool doesInputListContain(object list,Microsoft.Xna.Framework.Input.Keys key)=>false; }
 public class Input { public Microsoft.Xna.Framework.Input.GamePadState State; public Microsoft.Xna.Framework.Input.GamePadState GetGamePadState()=>State; }
 public static class Game1 { public static Menus.IClickableMenu? activeClickableMenu; public static object mouseCursors=new(),smallFont=new(); public static Options options=new(); public static Input input=new(); public static Microsoft.Xna.Framework.Input.GamePadState oldPadState; public static void playSound(string s){} public static void exitActiveMenu()=>activeClickableMenu=null; }
}
namespace StardewValley.Menus {
 using Microsoft.Xna.Framework;
 using Microsoft.Xna.Framework.Graphics;
 public class ClickableComponent { public Rectangle bounds; public string name; public int myID,leftNeighborID,rightNeighborID,downNeighborID; public bool tryDefaultIfNoDownNeighborExists,fullyImmutable; public bool containsPoint(int x,int y)=>bounds.Contains(x,y); public ClickableComponent(Rectangle b,string n,string label){bounds=b;name=n;} }
 public class IClickableMenu {
 public int xPositionOnScreen,yPositionOnScreen,width=880,height=680; public ClickableComponent? upperRightCloseButton;
 public IClickableMenu(){} public IClickableMenu(int x,int y,int w,int h){xPositionOnScreen=x;yPositionOnScreen=y;width=w;height=h;}
 public virtual void update(GameTime time){} public virtual void draw(SpriteBatch b){} public virtual void receiveLeftClick(int x,int y,bool playSound=true){}
 public virtual void receiveRightClick(int x,int y,bool playSound=true){} public virtual void receiveScrollWheelAction(int direction){}
 public virtual void leftClickHeld(int x,int y){} public virtual void releaseLeftClick(int x,int y){}
 public virtual void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys key){} public virtual void receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons button){}
 public virtual void performHoverAction(int x,int y){} public virtual void gameWindowSizeChanged(Rectangle oldBounds,Rectangle newBounds){}
 public virtual void snapToDefaultClickableComponent(){} public virtual void setCurrentlySnappedComponentTo(int id){} public virtual void applyMovementKey(int direction){}
 public virtual void automaticSnapBehavior(int direction,int oldRegion,int oldId){} public void exitThisMenu()=>StardewValley.Game1.exitActiveMenu(); public IClickableMenu? Child; public List<ClickableComponent>? allClickableComponents=new(); public ClickableComponent? currentlySnappedComponent; public IClickableMenu? GetChildMenu()=>Child; public virtual bool readyToClose()=>true; public virtual ClickableComponent getCurrentlySnappedComponent()=>currentlySnappedComponent!; public virtual void snapCursorToCurrentSnappedComponent(){} public static void drawHoverText(SpriteBatch b,string text,object font)=>b.Events.Add("tooltip:"+text); }
 public class SkillsPage:IClickableMenu {} public class SocialPage:IClickableMenu {}
 public class CollectionsPage:IClickableMenu { public IClickableMenu? letterviewerSubMenu; }
 public class GameMenu:IClickableMenu { public Action? ForwardAction; public override void draw(SpriteBatch b)=>ForwardAction?.Invoke(); public override void update(GameTime t)=>ForwardAction?.Invoke(); public Dictionary<string,int> Mappings=new(); public List<IClickableMenu> pages=new(); public List<ClickableComponent> tabs=new(); public int currentTab; public bool invisible; public static bool forcePreventClose; public string hoverText=""; public int getTabNumberFromName(string s)=>Mappings.TryGetValue(s,out int mapped)?mapped:s switch {"skills"=>0,"social"=>1,"collections"=>2,_=>-1}; public Action<GameMenu,int>? BeforeChangeTab; public void changeTab(int i,bool sound){BeforeChangeTab?.Invoke(this,i);currentTab=getTabNumberFromName(tabs[i].name);} public void AddTabsToClickableComponents(IClickableMenu page)=>page.allClickableComponents!.AddRange(tabs); public void drawMouse(SpriteBatch b,bool always)=>b.Events.Add("mouse"); }
}
namespace MossHarbor.ProductionProgress.Menus {
 using StardewValley.Menus;
 public class ProgressPage:IClickableMenu { public ProgressPage(GameMenu m,StardewModdingAPI.TranslationHelper t,Func<IReadOnlyCollection<ProgressRow>> rows,Func<long> rev,Func<string?> status,Action opened){} public void ReleaseResources(){} public void Open(){} public void Scroll(int n){} public void populateClickableComponentList(){} }

}
