using System;
using UnityEngine;

[Serializable]
public class WeightedRandom
{
    [Range(0, 500)] public int MinQuantity;
    [Range(0, 500)] public int MaxQuantity;
    [Range(0, 1)] public float MinMaxWeightFactor;

    private Vector3 curveParams = new Vector3(578, -264, 32.5f);
    private Vector3 curveParams2 = new Vector3(25.005f, -27.5035f, 7.5006f);

    public WeightedRandom() {}

    public WeightedRandom(int inMinQuantity, int inMaxQuantity, float inMinMaxWeightFactor)
    {
        this.MinQuantity = inMinQuantity;
        this.MaxQuantity = inMaxQuantity;
        this.MinMaxWeightFactor = Mathf.Min(1, inMinMaxWeightFactor);
    }

    public int GetWeightedRandomQuantity(float inForcedValue = -1)
    {
        int quantity = this.MinQuantity;

        if (this.MinMaxWeightFactor >= 1)
            quantity = this.MaxQuantity;
        else if (this.MinMaxWeightFactor > 0)
        {
            float adjustedMin = this.MinQuantity - .5f;
            float adjustedMax = this.MaxQuantity + .5f;

            float delta = adjustedMax - adjustedMin;

            float rnd = UnityEngine.Random.value;

            float a = 0;

            if (this.MinMaxWeightFactor <= .2f)
            {
                //a=p1(1)*w^2+p1(2)*w+p1(3);
                a = curveParams.x * Mathf.Pow(this.MinMaxWeightFactor, 2) + (curveParams.y * this.MinMaxWeightFactor) + curveParams.z;
            }
            else if (0.2f < this.MinMaxWeightFactor && this.MinMaxWeightFactor <= .5f)
            {
                //a = p2(1) * w ^ 2 + p2(2) * w + p2(3);
                a = curveParams2.x * Mathf.Pow(this.MinMaxWeightFactor, 2) + curveParams2.y * this.MinMaxWeightFactor + curveParams2.z;
            }
            else if (.5f < this.MinMaxWeightFactor && this.MinMaxWeightFactor <= .8f)
            {
                //a=p2(1)*(1-w)^2+p2(2)*(1-w)+p2(3);
                a = curveParams2.x * Mathf.Pow(1 - this.MinMaxWeightFactor, 2) + curveParams2.y * (1 - this.MinMaxWeightFactor) + curveParams2.z;
            }
            else if (.8f < this.MinMaxWeightFactor)
            {
                //a=p1(1)*(1-w)^2+p1(2)*(1-w)+p1(3);
                a = curveParams.x * Mathf.Pow(1 - this.MinMaxWeightFactor, 2) + (curveParams.y * (1 - this.MinMaxWeightFactor)) + curveParams.z;
            }

            float k = a / (1 - Mathf.Exp(-a));//k=a/(1-exp(-a));

            float temp = -Mathf.Log(1 - (a / k) * rnd) / a;//temp=-log(1-(a/k)*u)/a;

            if (this.MinMaxWeightFactor <= .5)
                quantity = Mathf.RoundToInt(adjustedMin + delta * temp);//x=xmin+(xmax-xmin)*temp;
            else
                quantity = Mathf.RoundToInt(adjustedMin + delta * (1 - temp));//x=xmin+(xmax-xmin)*(1-temp);

            quantity = Mathf.Max(quantity, this.MinQuantity);
            quantity = Mathf.Min(quantity, this.MaxQuantity);

            if (quantity < this.MinQuantity)
                Debug.Log("Too Low!");
            else if (quantity > this.MaxQuantity)
                Debug.Log("Too High!");
        }

        return quantity;
    }
}
