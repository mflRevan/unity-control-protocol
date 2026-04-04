using UnityEngine;

[CreateAssetMenu(fileName = "GameConfig", menuName = "UCP/GameConfig")]
public class GameConfig : ScriptableObject
{
    public float moveSpeed = 5f;
    public int maxLives = 3;
    public Material playerMaterial;
    public GameObject collectiblePrefab;
}
