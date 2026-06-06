using UnityEditor;
using UnityEngine;

public class CardConfigGenerator : EditorWindow
{
    public string TargetPath;
    public string FileName;
    public int MinTraitValue;
    public int MaxTraitValue;

    [MenuItem("Window/Card Config Generator")]
    static void Init()
    {
        CardConfigGenerator window = (CardConfigGenerator)EditorWindow.GetWindow(typeof(CardConfigGenerator));
        window.Show();
    }

    public void CreateCardDatas()
    {
        //loop thru all 
        for (int bombElementTypes = 0; bombElementTypes < 4; bombElementTypes++)
        {
            for (int secondElementType = 0; secondElementType < 4; secondElementType++)
            {
                for (int bombVal = MinTraitValue; bombVal <= MaxTraitValue; bombVal++)
                {
                    for (int pointsVal = MinTraitValue; pointsVal <= MaxTraitValue; pointsVal++)
                    {
                        var cardConfig = ScriptableObject.CreateInstance<CardConfig>();
                        cardConfig.Bombs[bombElementTypes] = bombVal;
                        cardConfig.Points[secondElementType] = pointsVal;

                        var fileName = cardConfig.GetFileName();
                        cardConfig.name = fileName.ToString();
                        AssetDatabase.CreateAsset(cardConfig, $"{TargetPath}/{fileName}.asset");//$"Assets/YourSavePath/YourObject.asset"
                        EditorUtility.SetDirty(cardConfig);

                        //if (bombElementTypes == 0)
                        //{
                        //    var cardConfig2 = ScriptableObject.CreateInstance<CardConfig>();
                        //    cardConfig2.WildBomb = bombVal;
                        //    cardConfig2.Points[secondElementType] = pointsVal;

                        //    var fileName2 = cardConfig2.GetFileName();
                        //    cardConfig2.name = fileName2.ToString();
                        //    AssetDatabase.CreateAsset(cardConfig2, $"{TargetPath}/{fileName2}.asset");//$"Assets/YourSavePath/YourObject.asset"
                        //    EditorUtility.SetDirty(cardConfig2);
                        //}

                        //if (bombElementTypes == 0)
                        //{
                        //    var cardConfig3 = ScriptableObject.CreateInstance<CardConfig>();
                        //    cardConfig3.WildBomb = bombVal;
                        //    cardConfig3.WildPoints = pointsVal;

                        //    var fileName3 = cardConfig3.GetFileName();
                        //    cardConfig3.name = fileName3.ToString();
                        //    AssetDatabase.CreateAsset(cardConfig3, $"{TargetPath}/{fileName3}.asset");//$"Assets/YourSavePath/YourObject.asset"
                        //    EditorUtility.SetDirty(cardConfig3);
                        //}
                    }
                   
                    for (int multsVal = 2; multsVal <= 3; multsVal++)
                    {
                        var cardConfig = ScriptableObject.CreateInstance<CardConfig>();
                        cardConfig.Bombs[bombElementTypes] = bombVal;
                        cardConfig.Multipliers[secondElementType] = multsVal;

                        var fileName = cardConfig.GetFileName();
                        cardConfig.name = fileName.ToString();
                        AssetDatabase.CreateAsset(cardConfig, $"{TargetPath}/{fileName}.asset");//$"Assets/YourSavePath/YourObject.asset"
                        EditorUtility.SetDirty(cardConfig);

                        //if (bombElementTypes == 0)
                        //{
                        //    var cardConfig2 = ScriptableObject.CreateInstance<CardConfig>();
                        //    cardConfig2.WildBomb = bombVal;
                        //    cardConfig2.Multipliers[secondElementType] = multsVal;

                        //    var fileName2 = cardConfig2.GetFileName();
                        //    cardConfig2.name = fileName2.ToString();
                        //    AssetDatabase.CreateAsset(cardConfig2, $"{TargetPath}/{fileName2}.asset");//$"Assets/YourSavePath/YourObject.asset"
                        //    EditorUtility.SetDirty(cardConfig2);
                        //}

                        if (bombElementTypes == 0)
                        {
                            var cardConfig3 = ScriptableObject.CreateInstance<CardConfig>();
                            cardConfig3.WildBomb = bombVal;
                            cardConfig3.WildMultiplier = multsVal;

                            var fileName3 = cardConfig3.GetFileName();
                            cardConfig3.name = fileName3.ToString();
                            AssetDatabase.CreateAsset(cardConfig3, $"{TargetPath}/{fileName3}.asset");//$"Assets/YourSavePath/YourObject.asset"
                            EditorUtility.SetDirty(cardConfig3);
                        }
                    }


                    for (int shieldVal = 1; shieldVal <= MaxTraitValue; shieldVal++)
                    {
                        var cardConfig = ScriptableObject.CreateInstance<CardConfig>();
                        cardConfig.Bombs[bombElementTypes] = bombVal;
                        cardConfig.Shields[secondElementType] = shieldVal;

                        var fileName = cardConfig.GetFileName();
                        cardConfig.name = fileName.ToString();
                        AssetDatabase.CreateAsset(cardConfig, $"{TargetPath}/{fileName}.asset");//$"Assets/YourSavePath/YourObject.asset"
                        EditorUtility.SetDirty(cardConfig);


                        //if (bombElementTypes == 0)
                        //{
                        //    var cardConfig2 = ScriptableObject.CreateInstance<CardConfig>();
                        //    cardConfig2.WildBomb = bombVal;
                        //    cardConfig2.Shields[secondElementType] = shieldVal;

                        //    var fileName2 = cardConfig2.GetFileName();
                        //    cardConfig2.name = fileName2.ToString();
                        //    AssetDatabase.CreateAsset(cardConfig2, $"{TargetPath}/{fileName2}.asset");//$"Assets/YourSavePath/YourObject.asset"
                        //    EditorUtility.SetDirty(cardConfig2);
                        //}

                        //if (bombElementTypes == 0)
                        //{
                        //    var cardConfig3 = ScriptableObject.CreateInstance<CardConfig>();
                        //    cardConfig3.WildBomb = bombVal;
                        //    cardConfig3.WildShield = shieldVal;

                        //    var fileName3 = cardConfig3.GetFileName();
                        //    cardConfig3.name = fileName3.ToString();
                        //    AssetDatabase.CreateAsset(cardConfig3, $"{TargetPath}/{fileName3}.asset");//$"Assets/YourSavePath/YourObject.asset"
                        //    EditorUtility.SetDirty(cardConfig3);
                        //}
                    }
                }
            }
        }

        // And finally, prompt the editor database to save dirty assets, committing your changes to disk.
        AssetDatabase.SaveAssets();
    }

    void OnGUI()
    {
        GUILayout.Label("Generate CardConfigs", EditorStyles.boldLabel);

        TargetPath = EditorGUILayout.TextField("Target Path", $"{TargetPath}");
        //FileName = EditorGUILayout.TextField("FileName", $"{FileName}");
        //MaxFilesToCreate = Mathf.RoundToInt(EditorGUILayout.Slider("MaxFilesToCreate", MaxFilesToCreate, 1, 100));
        MinTraitValue = Mathf.RoundToInt(EditorGUILayout.Slider("MinTraitValue", MinTraitValue, 1, 4));
        MaxTraitValue = Mathf.RoundToInt(EditorGUILayout.Slider("MaxTraitValue", MaxTraitValue, 1, 5));

        if (GUILayout.Button("Generate"))
        {
            CreateCardDatas();
        }
    }
}