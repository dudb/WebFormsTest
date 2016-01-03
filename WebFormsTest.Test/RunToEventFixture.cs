﻿using Fritz.WebFormsTest.Test;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Fritz.WebFormsTest.Test
{

  /// <summary>
  /// A collection of tests that verify the Run to Event functionality
  /// </summary>
  [Collection("Precompiler collection")]
  public class RunToEventFixture : BaseFixture
  {

    public RunToEventFixture(PrecompilerFixture precompiler)
    {

    }

    [Fact]
    public void AllEventsInOrder()
    {

      // Arrange
      base.context.SetupGet(c => c.IsDebuggingEnabled).Returns(true);

      // Act
      var sut = WebApplicationProxy.GetPageByLocation<Web.Scenarios.RunToEvent.VerifyOrder>("/Scenarios/RunToEvent/VerifyOrder.aspx");
      sut.Context = context.Object;
      sut.RunToEvent();

      // Assert
      Assert.Equal(4, sut.EventList.Count);

      for (int i = 1; i <= 4; i++)
      {
        Assert.Equal(i.ToString(), sut.EventList[i].Substring(0, 1));
      }
      
    }

    [Fact]
    public void CantFireSameEventTwice()
    {

      // Arrange
      base.context.SetupGet(c => c.IsDebuggingEnabled).Returns(true);

      // Act
      var sut = WebApplicationProxy.GetPageByLocation<Web.Scenarios.RunToEvent.VerifyOrder>("/Scenarios/RunToEvent/VerifyOrder.aspx");
      sut.Context = context.Object;
      sut.FireEvent(TestablePage.WebFormEvent.Init);

      // Assert
      Assert.Throws<InvalidOperationException>(() => sut.FireEvent(TestablePage.WebFormEvent.Init));

    }
    
    [Theory]
    [InlineData(TestablePage.WebFormEvent.Init)]
    [InlineData(TestablePage.WebFormEvent.Load)]
    [InlineData(TestablePage.WebFormEvent.PreRender)]
    public void StopAtCorrectEvent(TestablePage.WebFormEvent evt)
    {

      // Arrange
      base.context.SetupGet(c => c.IsDebuggingEnabled).Returns(true);

      // Act
      var sut = WebApplicationProxy.GetPageByLocation<Web.Scenarios.RunToEvent.VerifyOrder>("/Scenarios/RunToEvent/VerifyOrder.aspx");
      sut.Context = context.Object;
      sut.RunToEvent(evt);

      // Assert
      int eventCount = (int)evt;
      Assert.Equal(eventCount, sut.EventList.Count);

    }

  }

}
