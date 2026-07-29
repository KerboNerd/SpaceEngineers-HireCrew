using System;
using HireCrew;
using Xunit;

public class CrewMissionMarkerRulesTests
{
    [Fact]
    public void CanViewerSee_Owner_Unfactioned()
    {
        Assert.True(CrewMissionMarkerRules.CanViewerSee(
            viewerIdentityId: 10,
            viewerFactionIdOrZero: 0,
            crewOwnerKey: 10,
            crewOwnerIsFaction: false,
            crewOwnerIdentityId: 10,
            crewOwnerFactionIdOrZero: 0));
    }

    [Fact]
    public void CanViewerSee_FactionMember_FactionOwnedCrew()
    {
        Assert.True(CrewMissionMarkerRules.CanViewerSee(
            viewerIdentityId: 11,
            viewerFactionIdOrZero: 99,
            crewOwnerKey: 99,
            crewOwnerIsFaction: true,
            crewOwnerIdentityId: 10,
            crewOwnerFactionIdOrZero: 99));
    }

    [Fact]
    public void CanViewerSee_FactionMember_PersonalCrewOfMate()
    {
        Assert.True(CrewMissionMarkerRules.CanViewerSee(
            viewerIdentityId: 11,
            viewerFactionIdOrZero: 99,
            crewOwnerKey: 10,
            crewOwnerIsFaction: false,
            crewOwnerIdentityId: 10,
            crewOwnerFactionIdOrZero: 99));
    }

    [Fact]
    public void CanViewerSee_Outsider_False()
    {
        Assert.False(CrewMissionMarkerRules.CanViewerSee(
            viewerIdentityId: 50,
            viewerFactionIdOrZero: 7,
            crewOwnerKey: 99,
            crewOwnerIsFaction: true,
            crewOwnerIdentityId: 10,
            crewOwnerFactionIdOrZero: 99));
    }

    [Fact]
    public void FormatLabel_Rounds_Meters()
    {
        Assert.Equal("Rex · 842 m", CrewMissionMarkerRules.FormatLabel("Rex", 842.4));
        Assert.Equal("Crew · 0 m", CrewMissionMarkerRules.FormatLabel(null, -3));
    }

    [Fact]
    public void ClampHudOffset_Leaves_Center_Alone()
    {
        float x = 10f;
        float y = -20f;
        CrewMissionMarkerRules.ClampHudOffset(ref x, ref y, 1920f, 1080f, 24f);
        Assert.Equal(10f, x);
        Assert.Equal(-20f, y);
    }

    [Fact]
    public void ClampHudOffset_Pins_Far_Right_To_Edge()
    {
        float x = 5000f;
        float y = 0f;
        CrewMissionMarkerRules.ClampHudOffset(ref x, ref y, 1920f, 1080f, 24f);
        Assert.Equal(1920f * 0.5f - 24f, x, 1);
        Assert.InRange(y, -1f, 1f);
    }

    [Fact]
    public void ClampDirToScreenEdge_Uses_View_Plane_Direction()
    {
        float x, y;
        CrewMissionMarkerRules.ClampDirToScreenEdge(1f, 0f, 1920f, 1080f, 24f, out x, out y);
        Assert.Equal(1920f * 0.5f - 24f, x, 1);
        Assert.InRange(y, -1f, 1f);
    }

    [Fact]
    public void ClampDirToScreenEdge_Tiny_Dir_Goes_To_Edge_Not_Center()
    {
        // Looking ~180°: right/up dots are tiny — must still land on the border.
        float x, y;
        CrewMissionMarkerRules.ClampDirToScreenEdge(0.001f, 0.0005f, 1920f, 1080f, 24f, out x, out y);
        float halfW = 1920f * 0.5f - 24f;
        float halfH = 1080f * 0.5f - 24f;
        bool onEdge = Math.Abs(Math.Abs(x) - halfW) < 1f || Math.Abs(Math.Abs(y) - halfH) < 1f;
        Assert.True(onEdge);
        Assert.True(Math.Abs(x) > 100f || Math.Abs(y) > 100f);
    }

    [Fact]
    public void ClampDirToScreenEdge_Zero_Dir_Defaults_To_Top()
    {
        float x, y;
        CrewMissionMarkerRules.ClampDirToScreenEdge(0f, 0f, 1920f, 1080f, 24f, out x, out y);
        Assert.Equal(0f, x, 1);
        Assert.Equal(1080f * 0.5f - 24f, y, 1);
    }
}
