using System;
using System.Reflection;
using Elemento.Domain.Net;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIActiveGameRowTests
{
    private GameObject rowObject;
    private GameObject localizationObject;

    [SetUp]
    public void SetUp()
    {
        PlayerData.Data = new PlayerData { LanguageIndex = 0 };
        this.localizationObject = new GameObject("Localization");
        LocalizationManager localization = this.localizationObject.AddComponent<LocalizationManager>();
        LocalizationManager.IN = localization;
        LOC.IN = localization;
    }

    [TearDown]
    public void TearDown()
    {
        if (this.rowObject != null)
            UnityEngine.Object.DestroyImmediate(this.rowObject);
        if (this.localizationObject != null)
            UnityEngine.Object.DestroyImmediate(this.localizationObject);
        LocalizationManager.IN = null;
        LOC.IN = null;
        PlayerData.Data = null;
    }

    [Test]
    public void Configure_FillsLocalizedTextAndInvokesCallbacks()
    {
        UIActiveGameRow row = CreateRow(out TextMeshProUGUI codeText,
            out TextMeshProUGUI opponentText, out TextMeshProUGUI rejoinText,
            out TextMeshProUGUI forfeitText, out Button rejoinButton, out Button forfeitButton);
        MyMatchData match = new()
        {
            MatchId = "match-id",
            InviteCode = "52MMXK",
            Status = "active",
            OpponentName = "Rival"
        };
        MyMatchData rejoined = null;
        MyMatchData forfeited = null;

        row.Configure(match, value => rejoined = value, value => forfeited = value);

        Assert.That(codeText.text, Is.EqualTo("Code: 52MMXK"));
        Assert.That(opponentText.text, Is.EqualTo("vs Rival"));
        Assert.That(rejoinText.text, Is.EqualTo("Rejoin"));
        Assert.That(forfeitText.text, Is.EqualTo("Forfeit"));

        rejoinButton.onClick.Invoke();
        forfeitButton.onClick.Invoke();

        Assert.That(rejoined, Is.SameAs(match));
        Assert.That(forfeited, Is.SameAs(match));
    }

    [Test]
    public void LanguageChange_RefreshesLabelsAndWaitingOpponent()
    {
        UIActiveGameRow row = CreateRow(out TextMeshProUGUI codeText,
            out TextMeshProUGUI opponentText, out TextMeshProUGUI rejoinText,
            out TextMeshProUGUI forfeitText, out _, out _);
        row.Configure(new MyMatchData
        {
            InviteCode = "52MMXK",
            Status = "waiting",
            OpponentName = string.Empty
        }, null, null);

        PlayerData.Data.LanguageIndex = 1;
        LocalizationManager.OnLanguageChanged?.Invoke(1);

        Assert.That(codeText.text, Is.EqualTo("Código: 52MMXK"));
        Assert.That(opponentText.text, Is.EqualTo("Esperando al rival"));
        Assert.That(rejoinText.text, Is.EqualTo("Volver a entrar"));
        Assert.That(forfeitText.text, Is.EqualTo("Abandonar"));
    }

    private UIActiveGameRow CreateRow(out TextMeshProUGUI codeText,
        out TextMeshProUGUI opponentText, out TextMeshProUGUI rejoinText,
        out TextMeshProUGUI forfeitText, out Button rejoinButton, out Button forfeitButton)
    {
        this.rowObject = new GameObject("Active Game Row", typeof(RectTransform));
        UIActiveGameRow row = this.rowObject.AddComponent<UIActiveGameRow>();
        codeText = CreateText("Code Text");
        opponentText = CreateText("Opponent Text");
        rejoinText = CreateText("Rejoin Text");
        forfeitText = CreateText("Forfeit Text");
        rejoinButton = CreateButton("Rejoin Button");
        forfeitButton = CreateButton("Forfeit Button");

        SetField(row, "codeText", codeText);
        SetField(row, "opponentText", opponentText);
        SetField(row, "rejoinButton", rejoinButton);
        SetField(row, "rejoinText", rejoinText);
        SetField(row, "forfeitButton", forfeitButton);
        SetField(row, "forfeitText", forfeitText);
        return row;
    }

    private TextMeshProUGUI CreateText(string inName)
    {
        GameObject textObject = new(inName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(this.rowObject.transform, false);
        return textObject.GetComponent<TextMeshProUGUI>();
    }

    private Button CreateButton(string inName)
    {
        GameObject buttonObject = new(inName, typeof(RectTransform), typeof(Button));
        buttonObject.transform.SetParent(this.rowObject.transform, false);
        return buttonObject.GetComponent<Button>();
    }

    private static void SetField(object inTarget, string inFieldName, object inValue)
    {
        FieldInfo field = inTarget.GetType().GetField(inFieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(inTarget, inValue);
    }
}
