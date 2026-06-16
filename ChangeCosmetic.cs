using UnityEngine;
using Photon.VR;

public class ChangeCosmetic : MonoBehaviour
{
    public enum CosmeticType
    {
        Head,
        Face,
        Body,
        LeftHand,
        RightHand
    }

    public CosmeticType type;

    // new keyword stops the Unity Object.name shadow warning
    // without changing the field name so inspector values are preserved
    public new string name;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("HandTag") && !other.CompareTag("ModTag"))
            return;

      

        // Apply on network
        PhotonVRManager.SetCosmetic(type.ToString(), name);
        
    }
}