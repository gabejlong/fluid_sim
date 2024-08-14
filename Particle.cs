using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Unity.Mathematics;

//using System.Numerics;
using UnityEngine;
using UnityEngine.UIElements;

public class Particle : MonoBehaviour
{
    float time;
    Vector2 randomDir;
    public int numParticles = 4;
    public float gravity = 0 ;
    public float particleSize = 0.1f;
    public float collisionDamping = 0.8f;
    public float particleSpacing = 0.3f;
    public float xBounds = 20;
    public float yBounds = 10;
    public float targetDensity = 1;
    public float pressureMultiplier = 1;
    public float smoothingRadius = 1f;
    Vector2 boundsSize;
    Vector2[] positions;
    Vector2[] predictedPositions;
    Vector2[] velocities; 
    float[] densities;
    uint3[] spatialLookup;
    uint[] startIndices;
    int2[] cellOffsets = new int2[]
        {
            new int2(-1, 1),
            new int2(0, 1),
            new int2(1, 1),
            new int2(-1, 0),
            new int2(0, 0),
            new int2(1, 0),
            new int2(-1, -1),
            new int2(0, -1),
            new int2(1, -1),
        };
    public GameObject lineRendererPrefab; // Prefab with a LineRenderer component
    private List<GameObject> lineRendererObjects = new List<GameObject>();

    static float smoothingKernel(float dist, float radius)
    {
        if (dist >= radius) return 0;
        float volume = (Mathf.PI * Mathf.Pow(radius, 4)) / 6;
        return (radius - dist) * (radius - dist) / volume;
    }
    static float smoothingKernelDerivative(float dst, float radius)
    {
        if (dst >= radius) return 0;
        float scale = 12 / (Mathf.Pow(radius, 4) * MathF.PI);
        return (dst - radius) * scale;
    }

    float calculateDensity(Vector2 position)
    {
        float density = 0;

        for (int i = 0; i < 9; i++)
        {
            int2 cellCoord = calcCellCoord(position, smoothingRadius) + cellOffsets[i];
            uint cellHash = calcCellHash(cellCoord);
            uint cellKey = getKeyFromHash(cellHash);
            for (uint currIndex = startIndices[cellKey]; currIndex<numParticles; currIndex++)
            {
                if (cellKey != spatialLookup[currIndex].z) break;
                if (cellHash != spatialLookup[currIndex].y) continue;
                float dist = (position - predictedPositions[spatialLookup[currIndex].x]).magnitude;
                density += smoothingKernel(dist, smoothingRadius);
            }
        }
        
        return density;
    }
    float convertDensityToPressure(float density)
    {
        return Mathf.Min(0, targetDensity - density) * pressureMultiplier;
    }

    Vector2 getRandomDir()
    {
        return UnityEngine.Random.insideUnitCircle;
    }

    float calculateSharedPressure(float densityA, float densityB)
    {
        float pressureA = convertDensityToPressure(densityA);
        float pressureB = convertDensityToPressure(densityB);
        return (pressureA + pressureB) / 2;
    }
    Vector2 calculatePressureForce(int particleIndex)
    {
        Vector2 pressureForce = Vector2.zero;
        for (int i = 0; i < 9; i++)
        {
            int2 cellCoord = calcCellCoord(predictedPositions[particleIndex], smoothingRadius) + cellOffsets[i];
            uint cellHash = calcCellHash(cellCoord);
            uint cellKey = getKeyFromHash(cellHash);
            for (uint currIndex = startIndices[cellKey]; currIndex<numParticles; currIndex++)
            {
                if (cellKey != spatialLookup[currIndex].z) break;
                if (cellHash != spatialLookup[currIndex].y) continue;
                uint otherParticleIndex = spatialLookup[currIndex].x;
                if (particleIndex == otherParticleIndex) continue;
                Vector2 offset = predictedPositions[particleIndex] - predictedPositions[otherParticleIndex];
                float dist = offset.magnitude;
                Vector2 dir = dist == 0 ? randomDir : offset / dist;
                float slope = smoothingKernelDerivative(dist, smoothingRadius);
                float density = densities[otherParticleIndex];
                float sharedPressure = calculateSharedPressure(density, densities[particleIndex]);
                pressureForce += sharedPressure * slope * dir / density;
            }
        }
        
        return pressureForce;
    }
    void Start()
    {
        boundsSize = new Vector2(xBounds, yBounds);
        positions = new Vector2[numParticles];
        predictedPositions = new Vector2[numParticles];
        velocities = new Vector2[numParticles];
        densities = new float[numParticles];
        spatialLookup = new uint3[numParticles];
        startIndices = new uint[numParticles];

        int particlesPerRow = (int)Mathf.Sqrt(numParticles);
        int particlesPerCol = (numParticles) / particlesPerRow;
        float spacing = particleSize * 2 + particleSpacing;
        for (int i = 0; i<numParticles; i++)
        {
            float x = (i % particlesPerRow - particlesPerRow / 2f + 0.5f) * spacing;
            float y = (i / particlesPerCol - particlesPerCol / 2f + 0.5f) * spacing;
            positions[i] = new Vector2(x, y);
        }
    }

    int2 calcCellCoord(Vector2 point, float smoothingRadius)
    {   
        return new int2((int)(point.x / smoothingRadius), (int)(point.y / smoothingRadius));
    }
    uint calcCellHash(int2 coord)
    {
        return (uint) (coord.x * 12289 + coord.y * 24593);
    }
    uint getKeyFromHash(uint hash)
    {
        return (uint) (hash % numParticles);
    }
    void updateSpatialLookup(Vector2[] points, float smoothingRadius)
    {
        Parallel.For (0, numParticles, i => {
            int2 cellCoord = calcCellCoord(points[i], smoothingRadius);
            uint cellHash = calcCellHash(cellCoord);
            uint cellKey = getKeyFromHash(cellHash);
            spatialLookup[i] = new uint3((uint)i, cellHash, cellKey);
            startIndices[i] = int.MaxValue;
        });  

        Array.Sort(spatialLookup, (a, b) => a.z.CompareTo(b.z));

        Parallel.For(0, numParticles, i => {
            uint key = spatialLookup[i].z;
            uint keyPrev = i == 0 ? int.MaxValue : spatialLookup[i - 1].z;
            if (key != keyPrev)
            {
                startIndices[key] = (uint)i;
            }
        });
    }
    void Update()
    {
        time = Time.deltaTime;
        randomDir = getRandomDir();
        clearPreviousCircles();
        Parallel.For(0, numParticles, i => {
            velocities[i] += Vector2.down * gravity * time;
            predictedPositions[i] = positions[i] + velocities[i] * 1 / 120;
        });
        updateSpatialLookup(predictedPositions, smoothingRadius);

        Parallel.For(0, numParticles, i => {
            densities[i] = calculateDensity(predictedPositions[i]);
        });
        Parallel.For(0, numParticles, i => {
            velocities[i] += calculatePressureForce(i) / densities[i] * time;
        });
        Parallel.For(0, numParticles, i => {
            positions[i] += velocities[i] * time;
            resolveCollisions(ref positions[i], ref velocities[i]);
        });

        drawCircle(positions);
    }

    void resolveCollisions(ref Vector2 position, ref Vector2 velocity)
    {
        Vector2 halfBoundsSize = boundsSize / 2 - Vector2.one * particleSize;

        if (Mathf.Abs(position.x) > halfBoundsSize.x)
        {
            position.x = halfBoundsSize.x * Mathf.Sign(position.x);
            velocity.x *= -1 * collisionDamping;
        }
        if (Mathf.Abs(position.y) > halfBoundsSize.y)
        {
            position.y = halfBoundsSize.y * Mathf.Sign(position.y);
            velocity.y *= -1 * collisionDamping;
        }
    }

    void clearPreviousCircles()
    {
         foreach (GameObject lineRendererObject in lineRendererObjects)
        {
            Destroy(lineRendererObject);
        }
        // Clear the list
        lineRendererObjects.Clear();
    }

    void drawCircle(Vector2[] positions)
    {
        int steps = 10;
        for (int i = 0; i < positions.Length; i++)
        {
            // Create a new LineRenderer for each circle
            GameObject lineRendererObject = Instantiate(lineRendererPrefab, transform);
            LineRenderer lineRenderer = lineRendererObject.GetComponent<LineRenderer>();
            lineRenderer.positionCount = steps + 1;
            lineRenderer.loop = true;
            lineRenderer.useWorldSpace = true;

            // Generate the points for the circle
            float angleStep = 2 * Mathf.PI / steps;
            for (int j = 0; j <= steps; j++)
            {
                float angle = j * angleStep;
                Vector2 point = new Vector2(
                    Mathf.Cos(angle) * particleSize,
                    Mathf.Sin(angle) * particleSize
                ) + positions[i];
                lineRenderer.SetPosition(j, point);
            }
            lineRendererObjects.Add(lineRendererObject);
        }
    }
}
