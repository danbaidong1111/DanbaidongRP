using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


[ExecuteInEditMode]
public class CalculateGaussianKernel : MonoBehaviour
{
    public float sigma = 1.0f;
    [Range(1.0f, 3.0f)]
    public float radiusScale = 3.0f;

    [Range(0,50)]
    public int PascalTriangleRows = 1;

    public bool showLastRow = true;

    [Range(0, 10)]
    public int DiscardValuesNum = 0;

    // Update is called once per frame
    void Update()
    {
        
    }
}


[CustomEditor(typeof(CalculateGaussianKernel))]
public class CalculateGaussianKernelEditor:Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        //EditorGUILayout.PropertyField(serializedObject.FindProperty("sigma"));
        if(GUILayout.Button("calculate gaussian weights"))
        {
            //ShowWeights(serializedObject.FindProperty("sigma").floatValue);
        }

        float[] weights = new float[serializedObject.FindProperty("PascalTriangleRows").intValue];
        if (GUILayout.Button("calculate PascalTrangle"))
        {
            
            CalcPascalTriangle(serializedObject.FindProperty("PascalTriangleRows").intValue,
                                        serializedObject.FindProperty("showLastRow").boolValue,
                                        ref weights);
        }

        if (GUILayout.Button("calc Sample Weight And Offset (sigma = 1)"))
        {
            CalcPascalTriangle(serializedObject.FindProperty("PascalTriangleRows").intValue,
                                        serializedObject.FindProperty("showLastRow").boolValue,
                                        ref weights);

            CalcLinearSampleWeightAndOffset(ref weights, serializedObject.FindProperty("DiscardValuesNum").intValue);
        }

        if (GUILayout.Button("calc Gaussian Sample Weight And Offset (with sigma)"))
        {
            List<float> w = CalcGaussWeightsNotNormalize(serializedObject.FindProperty("sigma").floatValue, serializedObject.FindProperty("radiusScale").floatValue);
            float[] ww = w.ToArray();
            CalcLinearSampleWeightAndOffset(ref ww, serializedObject.FindProperty("DiscardValuesNum").intValue);
        }

    }


    List<float> CalcGaussWeights(float sigma, float radiusScale)
    {
        List<float> weights = new List<float>();//定义高斯核权重矩阵
        int blurRadius = (int)Mathf.Ceil(radiusScale * sigma);//通过sigma计算模糊半径
        Debug.Log("blurRadius: " + blurRadius);
        for (int i = 0; i < (2 * blurRadius + 1); i++)
        {
            weights.Add(0.0f);
        }
        float towSigmaSquare = 2 * sigma * sigma;

        float weightSum = 0; //权重和

        Debug.Log(weights.Count);
        //计算每个权重（未归一化）
        for (int x = -blurRadius; x <= blurRadius; x++)
        {
            weights[x + blurRadius] = Mathf.Exp(-(x * x) / towSigmaSquare);
            weightSum += weights[x + blurRadius];
        }

        //归一化权重
        for (int i = 0; i < weights.Count; i++)
        {
            weights[i] /= weightSum;
        }

        return weights;
    }

    List<float> CalcGaussWeightsNotNormalize(float sigma, float radiusScale)
    {
        List<float> weights = new List<float>();//定义高斯核权重矩阵
        int blurRadius = (int)Mathf.Ceil(radiusScale * sigma);//通过sigma计算模糊半径
        Debug.Log("blurRadius: " + blurRadius);
        for (int i = 0; i < (2 * blurRadius + 1); i++)
        {
            weights.Add(0.0f);
        }
        float towSigmaSquare = 2 * sigma * sigma;

        float weightSum = 0; //权重和

        Debug.Log(weights.Count);
        //计算每个权重（未归一化）
        for (int x = -blurRadius; x <= blurRadius; x++)
        {
            weights[x + blurRadius] = Mathf.Exp(-(x * x) / towSigmaSquare);
            weightSum += weights[x + blurRadius];
        }

        return weights;
    }

    void ShowWeights(float sigma)
    {
        string result = "";
        List<float> w = CalcGaussWeights(sigma, 3.0f);
        for (int i = 0; i < w.Count; i++)
        {
            result += ("[" + i + "]: " +w[i].ToString("f8") + "\n");
        }
        Debug.Log(result);
    }

    void CalcPascalTriangle(int rows, bool showLastRow, ref float[] outWeights)
    {

        int length = rows;
        string result = "";


        int[,] array = new int[length, length];
        for (int i = 0; i < length; i++)
        { // 循环打印杨辉三角,length行

            for (int k = 0; k < length - i; k++) //打印空格
            {
                if (!showLastRow)
                    result += ("  ");
            }


            for (int j = 0; j <= i; j++) //注意:j<=i, 因为第1行有1列，第2行有2列，第3行有3列。。。
            {
                if (j == 0 || i == j)  //第一列和最后一列
                {
                    array[i, j] = 1; //值为1
                }
                else
                {
                    array[i, j] = array[i - 1, j - 1] + array[i - 1, j]; //中间列的值 = 上一行和它所在列-1的值 + 上一行和它所在列的值
                }



                if(showLastRow)
                {
                    if(i == length - 1)
                        result += (array[i, j].ToString() + " "); //打印值
                }
                else
                {
                    result += (array[i, j].ToString() + " "); //打印值
                }
            }
            if (showLastRow && i == length - 1)
            {
                result += ",rowNum: " + i;
            }
            result += "\n";//每行打印完所有值后换行
        }


        Debug.Log(result);

        for (int i = 0; i < length; i++)
        {
            outWeights[i] = array[length-1, i];
        }

    }

    void CalcLinearSampleWeightAndOffset(ref float[] nativeWeights, int discordNums)
    {
        if(nativeWeights.Length < discordNums * 2)
        {
            Debug.LogError("discord num is not appropriate or nativeWeights is null(try CalcPascalTriangle first)");
        }
        int weightsLength = nativeWeights.Length - 2 * discordNums;
        float[] weights = new float[weightsLength];

        float sum = 0;

        string result = "";
        for(int i = discordNums; i < nativeWeights.Length - discordNums; i++)
        {
            weights[i - discordNums] = nativeWeights[i];
            sum += nativeWeights[i];

            result += " " + nativeWeights[i];
        }
        Debug.Log(result);
        result = "";


        if (sum == 0)
        {
            Debug.LogError("sum is zero, check!");
        }
        else
        {
            Debug.Log("sum: " + sum);
        }

        float sum2 = 0;
        for (int i = 0; i < weightsLength; i++)
        {
            weights[i] /= sum;
            result += " " + weights[i];
            sum2 += weights[i];
        }

        Debug.Log("sum: " + sum2 + ", oriWeights: " + result);
        result = "";

        float centerSample = (weightsLength - 1) / 2.0f;

        int resultLength = (int)Mathf.Ceil(weightsLength / 2.0f);
        resultLength = (int)Mathf.Ceil(resultLength / 2.0f);

        float[] resultWeights = new float[resultLength];
        float[] resultOffset = new float[resultLength];
        
        for (int i = 0; i < resultLength; i++)
        {
            resultWeights[i] = weights[2 * i] + weights[2 * i + 1];
            float offset1 = centerSample - 2 * i;
            float offset2 = centerSample - (2 * i + 1);
            resultOffset[i] = (offset1 * weights[2 * i] + offset2 * weights[2 * i + 1]) / resultWeights[i];
            if(2 * i == centerSample)
            {
                resultWeights[i] = weights[2 * i];
                resultOffset[i] = 0.0f;
            }

            result +=" [" + i + "]offset: " + resultOffset[i].ToString("f9") + ", weight: " + resultWeights[i].ToString("f9") + "\n";
        }
        Debug.Log("Results: \n" + result);
    }
}
