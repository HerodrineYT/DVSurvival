using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Serialization;
using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class HudDraggingTests
    {
        [Fact]
        public void HoldArmsAfterTwoSecondsAndPreservesGrabOffset()
        {
            var drag=new HudDragController();
            drag.Begin(0,new HudRect(20,30,200,100),80,65,10);
            Assert.False(drag.Move(80,65,11.99f,1280,720));
            Assert.False(drag.IsDragging); Assert.False(drag.HasMoved);
            Assert.Equal(.5f,drag.HoldProgress(11));
            Assert.True(drag.Move(180,115,12,1280,720));
            Assert.True(drag.IsDragging); Assert.True(drag.HasMoved);
            Assert.Equal(120,drag.Rect.X); Assert.Equal(80,drag.Rect.Y);
            drag.Cancel(); Assert.Equal(-1,drag.Panel); Assert.False(drag.HasMoved);
        }

        [Fact]
        public void EarlyMovementOrShortClickDoesNotMovePanels()
        {
            var drag=new HudDragController();
            drag.Begin(0,new HudRect(0,0,100,100),10,10,0);
            Assert.False(drag.Move(50,50,1,1280,720)); Assert.Equal(-1,drag.Panel);
            drag.Begin(0,new HudRect(0,0,100,100),10,10,5);
            drag.Cancel(); Assert.False(drag.Move(50,50,8,1280,720));
            drag.Begin(0,new HudRect(0,0,100,100),1000,10,0); Assert.Equal(-1,drag.Panel);
        }

        [Fact]
        public void DragCannotLosePanelsBeyondTheScreenEdges()
        {
            var drag=new HudDragController();
            drag.Begin(1,new HudRect(20,30,200,100),80,65,0);
            drag.Move(10000,-10000,3,1280,720);
            Assert.Equal(1080,drag.Rect.X); Assert.Equal(0,drag.Rect.Y);
            drag.Move(-10000,10000,4,1280,720);
            Assert.Equal(0,drag.Rect.X); Assert.Equal(620,drag.Rect.Y);
        }

        [Fact]
        public void PositionsRoundTripIndependentlyForEveryStyleAndPanel()
        {
            var positions=HudPlacement.ValidatePositions(null);
            for(int i=0;i<positions.Length;i++)
            { positions[i].IsCustom=true; positions[i].X=i/20f; positions[i].Y=1-i/20f; }
            var serializer=new XmlSerializer(typeof(HudPanelPosition[]));
            string xml;
            using(var text=new StringWriter()) { serializer.Serialize(text,positions); xml=text.ToString(); }
            HudPanelPosition[] loaded;
            using(var text=new StringReader(xml)) loaded=(HudPanelPosition[])serializer.Deserialize(text);
            for(int i=0;i<positions.Length;i++)
            { Assert.True(loaded[i].IsCustom); Assert.Equal(positions[i].X,loaded[i].X); Assert.Equal(positions[i].Y,loaded[i].Y); }
            loaded[HudPlacement.Index(2,HudPlacement.Air)].Reset();
            Assert.False(loaded[HudPlacement.Index(2,HudPlacement.Air)].IsCustom);
            Assert.True(loaded[HudPlacement.Index(2,HudPlacement.Needs)].IsCustom);
            Assert.True(loaded[HudPlacement.Index(3,HudPlacement.Air)].IsCustom);
        }

        [Theory]
        [InlineData(640,480,1.75f)] [InlineData(1920,1080,.65f)] [InlineData(2560,1440,1)]
        public void RememberedAnchorsRemainVisibleAfterResolutionOrScaleChanges(float width,float height,float requested)
        {
            var saved=new HudPanelPosition();
            HudPlacement.Remember(saved,new HudRect(700,300,338,134),1280,720);
            var scale=HudLayout.FitScale(0,requested,width,height);
            var rect=HudPlacement.Resolve(saved,HudPlacement.Default(0,0,scale,width,height),width,height);
            Assert.InRange(rect.X,0,width-rect.Width); Assert.InRange(rect.Y,0,height-rect.Height);
            Assert.Equal(saved.X,rect.X/(width-rect.Width),4);
            Assert.Equal(saved.Y,rect.Y/(height-rect.Height),4);
        }

        [Fact]
        public void InvalidOrOlderSettingsDoNotDiscardOtherPositions()
        {
            var old=new[] { new HudPanelPosition { IsCustom=true,X=.5f,Y=.25f },
                new HudPanelPosition { IsCustom=true,X=float.NaN,Y=100 } };
            var valid=HudPlacement.ValidatePositions(old);
            Assert.Equal(HudPlacement.PositionCount,valid.Length);
            Assert.True(valid[0].IsCustom); Assert.Equal(.5f,valid[0].X);
            Assert.False(valid[1].IsCustom); Assert.Equal(0,valid[1].X);
            Assert.All(valid,p=>Assert.NotNull(p));
            Assert.Same(valid,HudPlacement.ValidatePositions(valid));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void MovingAirDoesNotMoveNeedsOrBody(int style)
        {
            var positions=HudPlacement.ValidatePositions(null);
            var before=new DVSurvival.Mod.SurvivalHud().Record(style,false,false,positions);
            var air=positions[HudPlacement.Index(style,HudPlacement.Air)];
            air.IsCustom=true; air.X=.7f; air.Y=.2f;
            var after=new DVSurvival.Mod.SurvivalHud().Record(style,false,false,positions);
            using(var a=JsonDocument.Parse(JsonSerializer.Serialize(before,new JsonSerializerOptions { IncludeFields=true })))
            using(var b=JsonDocument.Parse(JsonSerializer.Serialize(after,new JsonSerializerOptions { IncludeFields=true })))
            {
                var original=a.RootElement.EnumerateArray().ToArray(); var moved=b.RootElement.EnumerateArray().ToArray();
                Assert.Equal(original.Length,moved.Length);
                bool changed=false;
                for(int i=0;i<original.Length;i++)
                    if(original[i].GetProperty("panel").GetInt32()!=HudPlacement.Air)
                        Assert.Equal(original[i].GetRawText(),moved[i].GetRawText());
                    else changed|=original[i].GetProperty("rect").GetRawText()!=moved[i].GetProperty("rect").GetRawText();
                Assert.True(changed);
            }
        }

        [Fact]
        public void DragLoopDoesNotAllocateManagedMemory()
        {
            var drag=new HudDragController(); drag.Begin(0,new HudRect(20,30,200,100),80,65,0);
            for(int i=0;i<1000;i++) drag.Move(80+i%50,65,3,1280,720);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<10000;i++) drag.Move(80+i%50,65,3,1280,720);
            Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        }
    }
}
