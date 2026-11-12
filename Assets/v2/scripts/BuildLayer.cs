using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Une couche : sa grille d'occupation, ses objets posés et ses éléments visuels (lignes, sol).
/// Tout ce qui appartient à la couche est enfant de Root, exprimé en coordonnées locales.
/// Déplacer / mettre à l'échelle Root suffit donc à placer la couche en profondeur.
/// </summary>
public class BuildLayer
{
    public string Name;

    public GameObject Root;
    public Transform ObjectsRoot;
    public GridView GridView;
    public GroundView GroundView;

    public GridData Grid;
    public readonly Dictionary<int, GameObject> Views = new Dictionary<int, GameObject>();

    // Profondeur affichée (animée) et profondeur cible : 0 = premier plan, 1 = juste derrière, etc.
    public float Depth;
    public float DepthVelocity;
    public float TargetDepth;
}