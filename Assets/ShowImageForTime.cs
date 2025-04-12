using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.IO;
using System;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;

public class ShowImageForTime : MonoBehaviour
{
    [Header("Configuración de Visualización")]
    public float displayTime = 25f;
    public float recordInterval = 0.001f;
    public Image imageDisplay;  // Asigna el componente Image en el Inspector o se buscará automáticamente
    
    [Header("Configuración de Texto")]
    public Text imageNumberText; // Componente Text para mostrar "Imagen 1", "Imagen 2", etc.
    public float textDisplayTime = 5f; // Tiempo que se muestra el texto de la imagen
   
    [Header("Eye Tracking")]
    public float maxGazeDistance = 10f;
    public bool forceHeadGaze = false; // Forzar uso de head gaze si el eye tracking no funciona
    public bool useEyeGazePriority = true; // Usar Eye Gaze como prioridad

    [Header("Calibración")]
    public bool enableCalibration = true; // Habilitar fase de calibración para cada participante
    public int calibrationPoints = 9; // Número de puntos de calibración (recomendado: 5-9)
    public float calibrationPointDuration = 2f; // Tiempo que el participante debe mirar cada punto
    public GameObject calibrationPointPrefab; // Prefab para los puntos de calibración (o se creará uno)
    public float calibrationPointSize = 0.05f; // Tamaño del punto de calibración
    public Color calibrationPointColor = Color.red; // Color del punto de calibración
    private CalibrationData calibrationData; // Datos de calibración capturados

    [Header("Debug")]
    public bool showDebugLogs = true;
    public bool showDebugVisuals = false;  // Para visualizar raycasts, etc.
    public bool requestEyeCalibration = false; // Solicitar calibración de eye tracking antes de iniciar

    // Variables para debug visual (opcional)
    private GameObject debugSphere;
    private LineRenderer debugLine;

    private Sprite[] sprites;
    private Canvas canvas;
    private CanvasScaler canvasScaler;
    private string currentImageName;
    private string csvBasePath;
    private string currentFilePath;
    private bool isInitialized = false;

    // Datos de Eye Tracking
    private bool isEyeGazeValid = false;
    private DateTime timestamp;
    private Ray gazeRay;
    private GameObject hitObject;
    private Vector3 hitPosition;
    private Vector3 gazeOrigin;
    private Vector3 gazeDirection;
    private bool isUsingHeadGaze = false; // Nueva variable para rastrear si estamos usando head gaze

    // Variables para estadísticas
    private int totalGazeCount = 0;
    private int validGazeCount = 0;
    private int hitCount = 0;

    // Contador de registros
    private int recordCount = 0;

    private bool eyeTrackingWorking;
    private int testCount;
    private float testStartTime;
    
    private IEnumerator CheckEyeTrackingWorking()
    {
        eyeTrackingWorking = false;
        testCount = 0;
        testStartTime = Time.time;  // AHORA está bien, porque está dentro de un método
        
        while (Time.time - testStartTime < 3.0f)
        {
            UpdateEyeGaze();
            if (isEyeGazeValid && !isUsingHeadGaze)
            {
                testCount++;
            }
            yield return null;
        }
        
        eyeTrackingWorking = testCount > 50;
        
        Debug.Log($"Test de eye tracking: {testCount} muestras válidas en 3 segundos, funcionando: {eyeTrackingWorking}");
        
        if (!eyeTrackingWorking)
        {
            Debug.LogWarning("El eye tracking no parece estar funcionando correctamente. Se utilizará head gaze para la calibración.");
            forceHeadGaze = true;
        }
    }

    // Clase para almacenar datos de calibración
    [System.Serializable]
    public class CalibrationData
    {
        public float offsetX = 0f;
        public float offsetY = 0f;
        public float scaleX = 1f;
        public float scaleY = 1f;
        public float rotation = 0f;
        public bool isCalibrated = false;
        public Vector2[] referencePoints; // Puntos donde el usuario debería mirar
        public Vector2[] actualGazePoints; // Puntos donde el usuario realmente miró

        // Constructor
        public CalibrationData(int numPoints)
        {
            referencePoints = new Vector2[numPoints];
            actualGazePoints = new Vector2[numPoints];
        }


        public void CalculateCalibrationParameters()
        {
            if (referencePoints.Length < 3 || actualGazePoints.Length < 3)
            {
                Debug.LogError("No hay suficientes puntos para calcular la calibración");
                return;
            }
            
            // Calcular centroides
            Vector2 refCentroid = Vector2.zero;
            Vector2 gazeCentroid = Vector2.zero;
            for (int i = 0; i < referencePoints.Length; i++)
            {
                refCentroid += referencePoints[i];
                gazeCentroid += actualGazePoints[i];
            }
            refCentroid /= referencePoints.Length;
            gazeCentroid /= actualGazePoints.Length;

            Debug.Log($"Centroide ref: {refCentroid}, Centroide gaze: {gazeCentroid}");

            // Calcular dispersión para determinar escalas
            float refDispersionX = 0f, refDispersionY = 0f;
            float gazeDispersionX = 0f, gazeDispersionY = 0f;
            for (int i = 0; i < referencePoints.Length; i++)
            {
                refDispersionX += Mathf.Abs(referencePoints[i].x - refCentroid.x);
                refDispersionY += Mathf.Abs(referencePoints[i].y - refCentroid.y);
                gazeDispersionX += Mathf.Abs(actualGazePoints[i].x - gazeCentroid.x);
                gazeDispersionY += Mathf.Abs(actualGazePoints[i].y - gazeCentroid.y);
            }
            refDispersionX /= referencePoints.Length;
            refDispersionY /= referencePoints.Length;
            gazeDispersionX /= actualGazePoints.Length;
            gazeDispersionY /= actualGazePoints.Length;

            // Evitar división por cero
            // if (gazeDispersionX < 0.005f) gazeDispersionX = 0.005f;
            // if (gazeDispersionY < 0.005f) gazeDispersionY = 0.005f;

            //  Calcular factores de escala
            scaleX = refDispersionX / gazeDispersionX;
            scaleY = refDispersionY / gazeDispersionY;

            //  Calcular offsets basados en centroides y escala, con ligera variabilidad
            offsetX = (refCentroid.x - gazeCentroid.x * scaleX);
            offsetY = (refCentroid.y - gazeCentroid.y * scaleY);

            // 8. Añadir ligera rotación aleatoria
            rotation = 0f;

            // 9. Limitar valores para evitar transformaciones extremas
            scaleX = Mathf.Clamp(scaleX, 0.7f, 1.4f);
            scaleY = Mathf.Clamp(scaleY, 0.7f, 1.4f);
            offsetX = Mathf.Clamp(offsetX, -0.3f, 0.3f);
            offsetY = Mathf.Clamp(offsetY, -0.3f, 0.3f);

            // Verificar parámetros neutrales
            bool neutralParams = Mathf.Abs(offsetX) < 0.01f && Mathf.Abs(offsetY) < 0.01f &&
                                Mathf.Abs(scaleX - 1.0f) < 0.01f && Mathf.Abs(scaleY - 1.0f) < 0.01f;


            // Registrar valores únicos con timestamp para verificación
            string uniqueId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Debug.Log($"Calibración {uniqueId}: offsetX={offsetX:F3}, offsetY={offsetY:F3}, scaleX={scaleX:F3}, scaleY={scaleY:F3}, rotate={rotation:F3}");

            isCalibrated = true;
        }


        private bool IsValidCoordinate(Vector2 coord)
        {
            return !float.IsNaN(coord.x) && !float.IsNaN(coord.y) &&
                !float.IsInfinity(coord.x) && !float.IsInfinity(coord.y) &&
                coord.x >= 0f && coord.x <= 1f &&
                coord.y >= 0f && coord.y <= 1f;
        }

        // Método para aplicar la calibración a un punto de mirada
        public Vector2 ApplyCalibration(Vector2 rawGazePoint)
        {
            if (!isCalibrated) return rawGazePoint;

            // 1. Centrar el punto para aplicar escalas y rotación
            Vector2 centered = rawGazePoint;
            
            // 2. Aplicar escala
            centered.x *= scaleX;
            centered.y *= scaleY;
            
            // 3. Aplicar offset
            centered.x += offsetX;
            centered.y += offsetY;

            return centered;
        }

        // Método para convertir los parámetros a un diccionario para Python
        public string ToParameterString()
        {
            return $"offsetX={offsetX}, offsetY={offsetY}, scaleX={scaleX}, scaleY={scaleY}, rotate={rotation}";
        }
    }

    void Awake()
    {
        // Si no se asignó el componente Image, intenta buscarlo
        if (imageDisplay == null)
        {
            GameObject defaultImageObj = GameObject.Find("MiImagePorDefecto");
            if (defaultImageObj != null)
                imageDisplay = defaultImageObj.GetComponent<Image>();
            else
                Debug.LogWarning("No se encontró 'MiImagePorDefecto'. Asigna manualmente el componente Image.");
        }

        if (showDebugVisuals)
            SetupDebugObjects();

        Initialize();
    }

    private void SetupDebugObjects()
    {
        // Esfera para visualizar el punto de impacto
        debugSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        debugSphere.transform.localScale = Vector3.one * 0.01f;
        debugSphere.GetComponent<Renderer>().material.color = Color.red;
        debugSphere.SetActive(false);

        // Línea para visualizar el rayo de mirada
        debugLine = gameObject.AddComponent<LineRenderer>();
        debugLine.startWidth = 0.005f;
        debugLine.endWidth = 0.001f;
        debugLine.material = new Material(Shader.Find("Sprites/Default"));
        debugLine.startColor = Color.green;
        debugLine.endColor = Color.yellow;
        debugLine.positionCount = 2;
    }

    public void Initialize()
    {
        if (!isInitialized)
        {
            InitializeComponents();
            SetupDataPath();
            CheckEyeTrackingCapabilities();
            // Inicializar datos de calibración
            calibrationData = new CalibrationData(calibrationPoints);
            isInitialized = true;
            if (showDebugLogs)
                Debug.Log("Inicialización completada");
        }
    }

    private void CheckEyeTrackingCapabilities()
    {
        // Verificar si el seguimiento ocular está disponible y habilitado
        if (CoreServices.InputSystem?.EyeGazeProvider != null)
        {
            var eyeGazeProvider = CoreServices.InputSystem.EyeGazeProvider;
            
            // Intenta habilitarlo explícitamente si está disponible
            if (!eyeGazeProvider.IsEyeTrackingEnabled)
            {
                // Intentar forzar la activación de eye tracking si es posible
                Debug.Log("Eye tracking no está habilitado. Intentando habilitarlo...");
                
                // Algunos proveedores de eye tracking pueden tener métodos específicos para habilitarlo
                // Esta sección podría variar según la versión de MRTK
                
                // Intenta acceder a los datos de eye tracking aunque no esté reportado como habilitado
                Vector3 testGaze = eyeGazeProvider.GazeOrigin;
                Debug.Log($"Prueba de acceso a datos: {testGaze}");
            }
            
            if (showDebugLogs)
            {
                Debug.Log($"Eye Tracking habilitado: {eyeGazeProvider.IsEyeTrackingEnabled}");
                Debug.Log($"Eye Tracking datos validos: {eyeGazeProvider.IsEyeTrackingDataValid}");
                Debug.Log($"Eye Tracking calibracion valida: {(eyeGazeProvider.IsEyeCalibrationValid.HasValue ? eyeGazeProvider.IsEyeCalibrationValid.Value : false)}");
            }
            
            // IMPORTANTE: No forzar el head gaze automaticamente, intentaremos usar eye tracking de todos modos
            forceHeadGaze = false;
        }
        else
        {
            forceHeadGaze = true;
            if (showDebugLogs)
                Debug.Log("Eye Gaze Provider no disponible. Usando Head Gaze como alternativa.");
        }
    }

    private void SetupDataPath()
    {
        try
        {
            csvBasePath = Path.Combine(Application.persistentDataPath, "EyeTrackingData");
            if (!Directory.Exists(csvBasePath))
            {
                Directory.CreateDirectory(csvBasePath);
            }
            if (showDebugLogs)
                Debug.Log($"Directorio para datos: {csvBasePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error configurando el directorio: {ex.Message}");
        }
    }

    private void InitializeComponents()
    {
        // Buscar o crear Canvas
        canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Canvas[] canvases = FindObjectsOfType<Canvas>();
            if (canvases.Length > 0)
            {
                canvas = canvases[0];
                if (showDebugLogs)
                    Debug.Log($"Usando canvas existente: {canvas.name}");
            }
            else
            {
                GameObject canvasObj = new GameObject("EyeTrackingCanvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvasObj.AddComponent<GraphicRaycaster>();
                canvasScaler = canvasObj.AddComponent<CanvasScaler>();
                if (Camera.main != null)
                {
                    canvasObj.transform.SetParent(Camera.main.transform, false);
                    if (showDebugLogs)
                        Debug.Log("Canvas creado y asignado a la Main Camera");
                }
            }
        }
        else
        {
            canvasScaler = canvas.GetComponent<CanvasScaler>();
            if (canvasScaler == null)
                canvasScaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }

        ConfigureCanvas();
        ConfigureImage();

        if (imageNumberText == null)
        {
            GameObject textObj = new GameObject("ImageNumberText");
            textObj.transform.SetParent(canvas.transform, false);
            imageNumberText = textObj.AddComponent<Text>();
            
            // Configurar propiedades del texto
            imageNumberText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            imageNumberText.fontSize = 72;
            imageNumberText.alignment = TextAnchor.MiddleCenter;
            imageNumberText.color = Color.white;
            
            // Configurar RectTransform
            RectTransform textRT = imageNumberText.rectTransform;
            textRT.anchorMin = new Vector2(0.5f, 0.5f);
            textRT.anchorMax = new Vector2(0.5f, 0.5f);
            textRT.pivot = new Vector2(0.5f, 0.5f);
            textRT.anchoredPosition = Vector2.zero;
            textRT.sizeDelta = new Vector2(600, 200);
        }
        
        // Inicialmente ocultar el texto
        imageNumberText.gameObject.SetActive(false);
        
        // Preparar prefab para puntos de calibración si no existe
        if (calibrationPointPrefab == null)
        {
            // Crear un prefab simple para los puntos de calibración
            GameObject pointObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pointObj.name = "CalibrationPoint";
            pointObj.transform.localScale = Vector3.one * calibrationPointSize;
            
            // Agregar material y color
            Renderer renderer = pointObj.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Standard"));
                renderer.material.color = calibrationPointColor;
            }
            
            calibrationPointPrefab = pointObj;
            calibrationPointPrefab.SetActive(false);
            
            if (showDebugLogs)
                Debug.Log("Creado prefab para puntos de calibración");
        }
    }

    private void ConfigureCanvas()
    {
        // Configurar el Canvas en WorldSpace para integrarlo en el entorno 3D
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 999;

        if (Camera.main != null)
        {
            Transform camTransform = Camera.main.transform;
            // Posicionar el canvas a una distancia óptima para eye tracking (2.0-2.5m)
            Vector3 adjustedPosition = camTransform.position + camTransform.forward * 2.0f;
            adjustedPosition.y -= 0.02f; // Desplazar hacia abajo para compensar el corte superior
            canvas.transform.position = adjustedPosition;
            canvas.transform.rotation = camTransform.rotation;
        }
        // Ajustar la escala para que el canvas tenga un tamaño adecuado para HoloLens
        canvas.transform.localScale = new Vector3(0.0018f, 0.0018f, 0.0018f);
        if (showDebugLogs)
            Debug.Log($"Canvas posicionado en: {canvas.transform.position}, escala: {canvas.transform.localScale}");

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920, 1080);
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = 0.5f;
        canvas.worldCamera = Camera.main;
    }

    private void ConfigureImage()
    {
        if (imageDisplay == null)
        {
            Debug.LogError("ShowImageForTime: Componente Image no asignado");
            return;
        }
        RectTransform rt = imageDisplay.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        // Tamaño inicial; se ajustará con cada imagen
        rt.sizeDelta = new Vector2(800, 800);

        imageDisplay.preserveAspect = true;
        imageDisplay.raycastTarget = true;
        imageDisplay.color = Color.white;

        // Asegurar que tenga un collider para detección de raycast con tamaño adecuado
        BoxCollider collider = imageDisplay.gameObject.GetComponent<BoxCollider>();
        if (collider == null)
            collider = imageDisplay.gameObject.AddComponent<BoxCollider>();
        
        // Hacer el collider ligeramente más grande para facilitar la detección de hits
        collider.size = new Vector3(1.0f, 1.0f, 0.1f);
        collider.isTrigger = true;
    }

    void Start()
    {

        if (CoreServices.SpatialAwarenessSystem != null)
        {
            var spatialSystem = CoreServices.SpatialAwarenessSystem;
            spatialSystem.SuspendObservers();
            if (showDebugLogs)
                Debug.Log("Observadores de mapeo espacial suspendidos (MRTK).");
        }
        else
        {
            if (showDebugLogs)
                Debug.LogWarning("Sistema de mapeo espacial de MRTK no encontrado.");
        }
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (var obj in allObjects)
        {
            if (obj.name.Contains("Observer") || obj.name.Contains("Spatial Mapping"))
            {
                obj.SetActive(false);
                if (showDebugLogs)
                    Debug.Log($"Objeto desactivado: {obj.name}");
            }
        }

        // Verificar sistemas de entrada disponibles
        if (showDebugLogs)
        {
            Debug.Log($"MRTK InputSystem disponible: {CoreServices.InputSystem != null}");
            if (CoreServices.InputSystem != null)
                Debug.Log($"Eye Gaze Provider disponible: {CoreServices.InputSystem.EyeGazeProvider != null}");
        }
        
        // Si no hay sistema de entrada, forzar head gaze
        if (CoreServices.InputSystem == null)
            forceHeadGaze = true;

        // Cargar imágenes y comenzar la secuencia
        LoadImages();
        if (ValidateSetup())
        {
            if (imageDisplay != null)
                imageDisplay.enabled = false;
            
            Canvas.ForceUpdateCanvases();
            
            calibrationData = null;

            // Si la calibración está habilitada, iniciar el proceso de calibración antes de mostrar las imágenes
            if (enableCalibration && !forceHeadGaze)
            {
                StartCoroutine(RunCalibrationProcess());
            }
            else
            {
                StartCoroutine(ShowImagesSequentially());
            }
        }
    }

    // Nuevo método para el proceso de calibración
    private IEnumerator RunCalibrationProcess()
    {
        if (showDebugLogs)
            Debug.Log("Iniciando proceso de calibración...");

        yield return StartCoroutine(CheckEyeTrackingWorking());
        // Reiniciar completamente la calibración para cada nueva sesión
        calibrationData = new CalibrationData(calibrationPoints);

        // Ocultar la imagen principal durante la calibración
        if (imageDisplay != null)
            imageDisplay.enabled = false;      
        // Mostrar instrucciones de calibración
        imageNumberText.text = "Calibración\nSiga con la mirada el punto rojo";
        imageNumberText.gameObject.SetActive(true);
           // Asegurarse de que el texto de calibración sea visible
        RectTransform textRT = imageNumberText.rectTransform;
        textRT.anchoredPosition = new Vector2(0, 50); // Ajustar posición vertical si es necesario
        yield return new WaitForSeconds(3f);
        
        // Ocultar instrucciones
        imageNumberText.gameObject.SetActive(false);
        
        // Generar puntos de calibración
        Vector2[] calibrationPositions = GenerateCalibrationPoints(calibrationPoints);
        
        // Crear objeto para el punto de calibración
        // Crear un objeto de UI para el punto de calibración en lugar de un primitivo 3D
        GameObject calibPoint = new GameObject("CalibrationPoint");
        calibPoint.transform.SetParent(canvas.transform, false);

        // Añadir un componente Image para visualización
        Image pointImage = calibPoint.AddComponent<Image>();
        pointImage.color = Color.red;
        pointImage.raycastTarget = false; // No queremos que interfiera con raycasts

        // Configurar RectTransform
        RectTransform calibPointRect = pointImage.rectTransform;
        RectTransform pointRT = pointImage.rectTransform;
        pointRT.anchorMin = new Vector2(0.5f, 0.5f);
        pointRT.anchorMax = new Vector2(0.5f, 0.5f);
        pointRT.pivot = new Vector2(0.5f, 0.5f);
        pointRT.sizeDelta = new Vector2(40f, 40f); // Tamaño en píxeles, ajustar si es necesario

        // Asegurarse que sea visible
        calibPoint.SetActive(true);
        
        // Guardar posiciones de referencia
        for (int i = 0; i < calibrationPositions.Length; i++)
        {
            calibrationData.referencePoints[i] = calibrationPositions[i];
        }
        
        // Depuración
        Debug.Log($"Iniciando secuencia de {calibrationPositions.Length} puntos de calibración");


        // Realizar calibración con cada punto
        for (int i = 0; i < calibrationPositions.Length; i++)
        {
            // Posicionar el punto de calibración
            Vector2 normalizedPos = calibrationPositions[i];
            
            // Convertir posición normalizada a posición en el canvas
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            float canvasWidth = canvasRect.rect.width;
            float canvasHeight = canvasRect.rect.height;
            
            Vector2 canvasPos = new Vector2(
                (normalizedPos.x - 0.5f) * canvasWidth,
                (normalizedPos.y - 0.5f) * canvasHeight
            );
            
            // Actualizar posición del punto
            calibPointRect.anchoredPosition = canvasPos;
            
            // Añadir efecto de pulso para mejor visibilidad
            StartCoroutine(PulseCalibrationPoint(calibPoint));
            
            if (showDebugLogs)
                Debug.Log($"Punto de calibración {i+1}/{calibrationPositions.Length} en posición: {normalizedPos}");
            
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (var obj in allObjects)
            {
                // Buscar cualquier objeto que pueda ser un cursor
                if (obj.name.Contains("Cursor") || obj.name.Contains("Pointer") || 
                    obj.name.Contains("Gaze") || obj.name.Contains("Indicator"))
                {
                    obj.SetActive(false);
                    if (showDebugLogs)
                        Debug.Log($"Posible cursor desactivado: {obj.name}");
                }
            }
            // Esperar un momento para que el usuario fije la mirada
            yield return new WaitForSeconds(0.5f);
            
            // Recoger datos de mirada durante un período
            Vector2 averageGazePos = Vector2.zero;
            int validSamples = 0;
            float calibrationStartTime = Time.time;
            float maxCalibrationTime = calibrationPointDuration; // Límite de tiempo


            while (Time.time - calibrationStartTime < calibrationPointDuration)
            {
                // Actualizar datos de mirada
                UpdateEyeGaze();
                
                // Si tenemos un punto de impacto válido, añadirlo al promedio
                if (hitObject != null && isEyeGazeValid)
                {
                    // Convertir posición 3D a posición normalizada 2D en el canvas
                    Vector2 hitPos2D = WorldToCanvasPosition(hitPosition);
                    averageGazePos += hitPos2D;
                    validSamples++;
                }
                
                yield return null;
            }
            
            // Calcular posición promedio de la mirada si tenemos muestras válidas
            if (validSamples > 10)
            {
                averageGazePos /= validSamples;
                calibrationData.actualGazePoints[i] = averageGazePos;
                
                if (showDebugLogs)
                    Debug.Log($"Punto {i+1}: Posición objetivo={normalizedPos}, Posición mirada={averageGazePos}, Muestras={validSamples}");
            }
            else
            {
                // Si no tenemos muestras válidas, usar la posición ideal (podría ajustarse)
                Vector2 artificialGaze = normalizedPos + new Vector2(0.05f, 0.05f);
                calibrationData.actualGazePoints[i] = artificialGaze;
                Debug.LogWarning($"No se obtuvieron muestras válidas para el punto {i+1}");
            }
            
            // Breve pausa antes del siguiente punto
            yield return new WaitForSeconds(0.2f);
        }
        
        // Eliminar el punto de calibración
        Destroy(calibPoint);
        
        // Calcular parámetros de calibración
        calibrationData.CalculateCalibrationParameters();
        
        // Mostrar mensaje de finalización
        imageNumberText.text = "¡Calibración Completada!";
        imageNumberText.gameObject.SetActive(true);
        
        yield return new WaitForSeconds(2f);
        
        // Ocultar mensaje
        imageNumberText.gameObject.SetActive(false);
        
        // Guardar datos de calibración en un archivo CSV para análisis posterior
        SaveCalibrationData();
        
        // Continuar con la secuencia de imágenes
        StartCoroutine(ShowImagesSequentially());
    }

    private IEnumerator PulseCalibrationPoint(GameObject pointObj)
    {
        if (pointObj == null) yield break;
        
        RectTransform rt = pointObj.GetComponent<RectTransform>();
        if (rt == null) yield break;
        
        Vector2 originalSize = rt.sizeDelta;
        Vector2 maxSize = originalSize * 1.3f;
        float pulseSpeed = 2.0f;
        
        while (pointObj != null && pointObj.activeInHierarchy)
        {
            // Pulsar de tamaño normal a grande y volver
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * pulseSpeed;
                float pulseFactor = Mathf.Sin(t * Mathf.PI) * 0.5f + 0.5f;
                rt.sizeDelta = Vector2.Lerp(originalSize, maxSize, pulseFactor);
                yield return null;
            }
        }
    }
    // Método para convertir posición mundial a posición normalizada en el canvas
    private Vector2 WorldToCanvasPosition(Vector3 worldPos)
    {
        // Convertir posición mundial a posición local del canvas
        Vector3 localPos = canvas.transform.InverseTransformPoint(worldPos);
        
        // Normalizar la posición
        RectTransform canvasRT = canvas.GetComponent<RectTransform>();
        float width = canvasRT.rect.width;
        float height = canvasRT.rect.height;
        
        return new Vector2(
            (localPos.x / width) + 0.5f,
            (localPos.y / height) + 0.5f
        );
    }

    // Método para generar puntos de calibración distribuidos en el canvas
    private Vector2[] GenerateCalibrationPoints(int numPoints)
    {
        Vector2[] points;
        
        // Distribución fija para 9 puntos (3x3 grid)
        if (numPoints == 9)
        {
            points = new Vector2[9];
            float[] positions = { 0.2f, 0.5f, 0.75f };
            
            int index = 0;
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    points[index++] = new Vector2(positions[x], positions[y]);
                }
            }
        }
        else if (numPoints == 5)
        {
            points = new Vector2[5];
            points[0] = new Vector2(0.5f, 0.5f); // Centro
            points[1] = new Vector2(0.2f, 0.2f); // Esquina inferior izquierda
            points[2] = new Vector2(0.8f, 0.2f); // Esquina inferior derecha
            points[3] = new Vector2(0.2f, 0.8f); // Esquina superior izquierda
            points[4] = new Vector2(0.8f, 0.8f); // Esquina superior derecha
        }
        else
        {
            points = new Vector2[numPoints];
            for (int i = 0; i < numPoints; i++)
            {
                // Evitar posiciones muy cercanas a los bordes
                points[i] = new Vector2(
                    UnityEngine.Random.Range(0.1f, 0.9f),
                    UnityEngine.Random.Range(0.1f, 0.9f)
                );
            }
        }
        
        return points;
    }

    // Método para guardar datos de calibración
    private void SaveCalibrationData()
    {
        try
        {
            string sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"Calibration_Session_{sessionId}.csv";
            string filePath = Path.Combine(csvBasePath, fileName);
            
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine($"SessionID,{sessionId}");
                writer.WriteLine($"CalibrationTime,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine();
                writer.WriteLine("PointIndex,RefX,RefY,GazeX,GazeY");
                
                for (int i = 0; i < calibrationData.referencePoints.Length; i++)
                {
                    writer.WriteLine($"{i}," +
                                    $"{calibrationData.referencePoints[i].x}," +
                                    $"{calibrationData.referencePoints[i].y}," +
                                    $"{calibrationData.actualGazePoints[i].x}," +
                                    $"{calibrationData.actualGazePoints[i].y}");
                }
                
                writer.WriteLine();
                writer.WriteLine("CalibrationParameters");
                writer.WriteLine($"OffsetX,{calibrationData.offsetX}");
                writer.WriteLine($"OffsetY,{calibrationData.offsetY}");
                writer.WriteLine($"ScaleX,{calibrationData.scaleX}");
                writer.WriteLine($"ScaleY,{calibrationData.scaleY}");
                writer.WriteLine($"Rotation,{calibrationData.rotation}");
            }
            
            if (showDebugLogs)
                Debug.Log($"Datos de calibración guardados en: {filePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error guardando datos de calibración: {ex.Message}");
        }
    }

    private void LoadImages()
    {
        if (showDebugLogs)
            Debug.Log("Cargando imágenes desde Resources/Images...");
        sprites = Resources.LoadAll<Sprite>("Images");
        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogError("No se encontraron imágenes en Resources/Images");
            return;
        }
        if (showDebugLogs)
        {
            Debug.Log($"Se encontraron {sprites.Length} imágenes:");
            foreach (var sprite in sprites)
            {
                if (sprite != null)
                    Debug.Log($"- {sprite.name} ({sprite.rect.width}x{sprite.rect.height})");
            }
        }
    }

    private bool ValidateSetup()
    {
        bool isValid = true;
        if (imageDisplay == null)
        {
            Debug.LogError("Error: Componente Image no encontrado");
            isValid = false;
        }
        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogError("Error: No hay imágenes cargadas");
            isValid = false;
        }
        if (isValid && imageDisplay != null && sprites.Length > 0)
        {
            imageDisplay.sprite = sprites[0];
            imageDisplay.enabled = false;
            if (showDebugLogs)
                Debug.Log($"Primera imagen asignada: {sprites[0].name}");
        }
        return isValid;
    }
    private void ForceNewCalibration()
    {
        calibrationData = null;
    }

    private IEnumerator ShowImagesSequentially()
    {
        if (showDebugLogs)
            Debug.Log("Iniciando secuencia de imágenes");
        
        if (requestEyeCalibration && !forceHeadGaze)
        {
     
            yield return new WaitForSeconds(6f); // Dar tiempo para la calibración inicial
        }
        
;
        
        // Mostrar cada imagen durante el tiempo configurado
        for (int i = 0; i < sprites.Length; i++)
        {
            Sprite sprite = sprites[i];
            if (sprite == null) continue;
            
            // Reiniciar contadores para esta imagen
            totalGazeCount = 0;
            validGazeCount = 0;
            hitCount = 0;
            
            imageDisplay.enabled = false; // Ocultar la imagen actual
            imageNumberText.text = $"Imagen {i + 1}";
            imageNumberText.gameObject.SetActive(true);
            
            if (showDebugLogs)
                Debug.Log($"Mostrando texto: {imageNumberText.text}");
            
            yield return new WaitForSeconds(textDisplayTime);
            
            imageNumberText.gameObject.SetActive(false);
            imageDisplay.enabled = true;
            
            if (showDebugLogs)
                Debug.Log($"Mostrando imagen: {sprite.name}");
            
            imageDisplay.sprite = sprite;
            currentImageName = sprite.name;
            
            CreateNewCSVFile(sprite.name);
            
            
            if (i == 0)
            {
                StartCoroutine(RecordEyeTrackingData());
                if (showDebugLogs)
                    Debug.Log("Iniciando grabación de datos para la primera imagen");
            }
            
            AdjustImageSize(sprite);

            yield return new WaitForSeconds(displayTime);
            
            float validPercent = totalGazeCount > 0 ? (validGazeCount * 100f / totalGazeCount) : 0;
            float hitPercent = totalGazeCount > 0 ? (hitCount * 100f / totalGazeCount) : 0;
            
            if (showDebugLogs)
                Debug.Log($"Estadísticas para {sprite.name}: Miradas válidas: {validPercent:F1}%, Hits: {hitPercent:F1}%");
        }
        
        if (showDebugLogs)
            Debug.Log("Secuencia de imágenes completada");
        
        currentImageName = null;
        yield return new WaitForSeconds(2f);
        
        if (showDebugLogs)
            Debug.Log("Cerrando aplicación...");
            
    #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
    #else
        Application.Quit();
    #endif
    }

    private void AdjustImageSize(Sprite sprite)
    {
        if (sprite == null) return;
        RectTransform rt = imageDisplay.rectTransform;
        

        float maxWidth = 650f;
        float maxHeight = 650f;
        float spriteWidth = sprite.rect.width;
        float spriteHeight = sprite.rect.height;
        float widthRatio = maxWidth / spriteWidth;
        float heightRatio = maxHeight / spriteHeight;
        float scaleFactor = Mathf.Min(widthRatio, heightRatio);
        
        scaleFactor *= 0.75f;
        Vector2 newSize = new Vector2(spriteWidth * scaleFactor, spriteHeight * scaleFactor);
        rt.sizeDelta = newSize;
        
        if (showDebugLogs)
            Debug.Log($"Imagen ajustada a: {rt.sizeDelta}");
        
        BoxCollider collider = imageDisplay.gameObject.GetComponent<BoxCollider>();
        if (collider != null && canvas != null)
        {
            Vector3 canvasScale = canvas.transform.localScale;
            
            float colliderWidth = newSize.x * canvasScale.x * 1.1f;
            float colliderHeight = newSize.y * canvasScale.y * 1.1f;
            collider.size = new Vector3(colliderWidth * 1.5f, colliderHeight * 1.5f, 0.1f);
            
            if (showDebugLogs)
                Debug.Log($"Collider actualizado a: {collider.size}");
        }
    }

    private string GetCSVFilePath(string imageName)
    {
        string fileName = $"{imageName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return Path.Combine(csvBasePath, fileName);
    }

    private void CreateNewCSVFile(string imageName)
    {
        try
        {
            string sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{imageName}_Session_{sessionId}.csv";
            currentFilePath = Path.Combine(csvBasePath, fileName);
            
            string header = $"SessionID,{sessionId}\n";
            header += "Timestamp,ImageName,EyeOriginX,EyeOriginY,EyeOriginZ," +
                    "GazeDirectionX,GazeDirectionY,GazeDirectionZ," +
                    "IsHitting,HitPositionX,HitPositionY,HitPositionZ," +
                    "IsEyeGazeValid,IsUsingHeadGaze,HeadPositionX,HeadPositionY,HeadPositionZ," +
                    "HeadRotationX,HeadRotationY,HeadRotationZ";
                    
            if (calibrationData != null && calibrationData.isCalibrated)
            {
                header += ",CalibrationOffsetX,CalibrationOffsetY,CalibrationScaleX,CalibrationScaleY,CalibrationRotation";
            }
            
            header += "\n";
                                        
            File.WriteAllText(currentFilePath, header);
            
            if (showDebugLogs)
                Debug.Log($"CSV creado para sesión {sessionId}: {currentFilePath}");
                
            recordCount = 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creando CSV: {ex.Message}");
            currentFilePath = null;
        }
    }

    private IEnumerator RecordEyeTrackingData()
    {
        if (showDebugLogs)
            Debug.Log("Iniciando grabación de datos de eye tracking y movimiento de cabeza");
            
        while (!string.IsNullOrEmpty(currentImageName))
        {
            UpdateEyeGaze();
            
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                // Obtener datos de la cabeza
                Vector3 headPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                Vector3 headRot = Camera.main != null ? Camera.main.transform.eulerAngles : Vector3.zero;

                // Actualizar contadores para estadísticas
                totalGazeCount++;
                if (isEyeGazeValid) validGazeCount++;
                if (hitObject != null) hitCount++;

                // Construir línea de datos CSV
                string dataLine = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                                  $"{currentImageName}," +
                                  $"{gazeOrigin.x:F6},{gazeOrigin.y:F6},{gazeOrigin.z:F6}," +
                                  $"{gazeDirection.x:F6},{gazeDirection.y:F6},{gazeDirection.z:F6}," +
                                  $"{(hitObject != null)}," +
                                  $"{hitPosition.x:F6},{hitPosition.y:F6},{hitPosition.z:F6}," +
                                  $"{isEyeGazeValid}," +
                                  $"{isUsingHeadGaze}," +
                                  $"{headPos.x:F6},{headPos.y:F6},{headPos.z:F6}," +
                                  $"{headRot.x:F6},{headRot.y:F6},{headRot.z:F6}";
                                  
                // Agregar datos de calibración si están disponibles
                if (calibrationData != null && calibrationData.isCalibrated)
                {
                    dataLine += $",{calibrationData.offsetX:F6},{calibrationData.offsetY:F6},{calibrationData.scaleX:F6},{calibrationData.scaleY:F6},{calibrationData.rotation:F6}";
                }
                
                dataLine += "\n";
                
                try
                {
                    File.AppendAllText(currentFilePath, dataLine);
                    recordCount++;
                    
                    // Mostrar estadísticas periódicamente
                    if (recordCount % 100 == 0 && showDebugLogs)
                    {
                        float validPercent = totalGazeCount > 0 ? (validGazeCount * 100f / totalGazeCount) : 0;
                        float hitPercent = totalGazeCount > 0 ? (hitCount * 100f / totalGazeCount) : 0;
                        Debug.Log($"Grabados {recordCount} registros. Válidos: {validPercent:F1}%, Hits: {hitPercent:F1}%");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error grabando datos: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning("currentFilePath vacío, no se pueden grabar datos");
            }
            
            yield return new WaitForSeconds(recordInterval);
        }
        
        if (showDebugLogs)
            Debug.Log($"Grabación finalizada. Total registros: {recordCount}");
    }

    void Update()
    {
        if (!isInitialized) return;
        
        UpdateEyeGaze();
        
        // Actualizar visualizaciones de debug si están habilitadas
        if (showDebugVisuals)
        {
            UpdateDebugVisuals();
        }
    }

    private void UpdateDebugVisuals()
    {
        if (debugLine != null && debugSphere != null)
        {
            debugLine.SetPosition(0, gazeOrigin);
            debugLine.SetPosition(1, gazeOrigin + gazeDirection * maxGazeDistance);
            debugLine.enabled = true;
            
            if (hitObject != null)
            {
                debugSphere.transform.position = hitPosition;
                debugSphere.SetActive(true);
            }
            else
            {
                debugSphere.SetActive(false);
            }
        }
    }

    private void UpdateEyeGaze()
    {
        try
        {
            // Reiniciar valores por defecto
            gazeOrigin = Vector3.zero;
            gazeDirection = Vector3.forward;
            hitObject = null;
            hitPosition = Vector3.zero;
            isEyeGazeValid = false;
            isUsingHeadGaze = false;
            timestamp = DateTime.Now;

            if (forceHeadGaze || CoreServices.InputSystem?.EyeGazeProvider == null)
            {
                UpdateHeadGaze();
                return;
            }

            // Intentar obtener datos de eye tracking
            var eyeGazeProvider = CoreServices.InputSystem.EyeGazeProvider;
            
            // Siempre intenta obtener datos del eye tracking
            gazeOrigin = eyeGazeProvider.GazeOrigin;
            gazeDirection = eyeGazeProvider.GazeDirection;

            // Verificación más permisiva para considerar datos válidos
            bool dataLooksValid = gazeDirection.magnitude > 0.01f && 
                                !float.IsNaN(gazeDirection.x) && 
                                !float.IsInfinity(gazeDirection.x);


            isEyeGazeValid = dataLooksValid;

            if (dataLooksValid)
            {
                gazeRay = new Ray(gazeOrigin, gazeDirection);
                timestamp = DateTime.Now;
                PerformRaycasts();
                return;
            }
            else
            {
                UpdateHeadGaze();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error en UpdateEyeGaze: {ex.Message}");
            UpdateHeadGaze();
        }
    }

    private void UpdateHeadGaze()
    {
        if (Camera.main == null)
            return;
            
        Ray headRay = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        isEyeGazeValid = false; 
        isUsingHeadGaze = true;
        timestamp = DateTime.Now;
        gazeRay = headRay;
        gazeOrigin = headRay.origin;
        gazeDirection = headRay.direction;
        PerformRaycasts();
    }

    private void PerformRaycasts()
    {
        RaycastHit hitInfo;
        
        if (Physics.Raycast(gazeRay, out hitInfo, maxGazeDistance))
        {
            hitObject = hitInfo.collider.gameObject;
            hitPosition = hitInfo.point;
            
            if (calibrationData != null && calibrationData.isCalibrated && !isUsingHeadGaze)
            {
                Vector2 hitNormalized = WorldToCanvasPosition(hitPosition);
                
                Vector2 calibratedPos = calibrationData.ApplyCalibration(hitNormalized);
                
                hitPosition = CanvasToWorldPosition(calibratedPos);
            }
            
            return;
        }
        
        if (imageDisplay != null)
        {
            BoxCollider collider = imageDisplay.GetComponent<BoxCollider>();
            if (collider != null && collider.Raycast(gazeRay, out hitInfo, maxGazeDistance))
            {
                hitObject = imageDisplay.gameObject;
                hitPosition = hitInfo.point;
                
                if (calibrationData != null && calibrationData.isCalibrated && !isUsingHeadGaze)
                {
                    Vector2 hitNormalized = WorldToCanvasPosition(hitPosition);
                    
                    Vector2 calibratedPos = calibrationData.ApplyCalibration(hitNormalized);
                    
                    hitPosition = CanvasToWorldPosition(calibratedPos);
                }
                
                return;
            }
        }
        
        hitObject = null;
        hitPosition = Vector3.zero;
    }
    
    private Vector3 CanvasToWorldPosition(Vector2 normalizedPos)
    {
        Vector2 centeredPos = new Vector2(
            (normalizedPos.x - 0.5f) * canvas.GetComponent<RectTransform>().rect.width,
            (normalizedPos.y - 0.5f) * canvas.GetComponent<RectTransform>().rect.height
        );
        
        return canvas.transform.TransformPoint(new Vector3(centeredPos.x, centeredPos.y, 0));
    }

    void LateUpdate()
    {
        if (canvas != null && Camera.main != null)
        {
            Transform camTransform = Camera.main.transform;
            Vector3 adjustedPosition = camTransform.position + camTransform.forward * 2.2f;
            adjustedPosition.y -= 0.05f;
            canvas.transform.position = adjustedPosition;
            canvas.transform.rotation = camTransform.rotation;
        }
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || !showDebugVisuals) return;
        
        Gizmos.color = isEyeGazeValid ? Color.blue : Color.yellow;
        Gizmos.DrawRay(gazeOrigin, gazeDirection * maxGazeDistance);
        
        if (hitObject != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(hitPosition, 0.02f);
        }
    }
}