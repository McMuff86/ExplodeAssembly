using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.DocObjects;
using System.Drawing;
using Eto.Forms;
using Eto.Drawing;

namespace ExplodeAssembly
{
    public class ExplodeAssembly : Rhino.Commands.Command
    {
        public override string EnglishName => "ExplodeAssembly";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Masterblock auswählen
            var go = new Rhino.Input.Custom.GetObject();
            go.SetCommandPrompt("Wählen Sie den Masterblock aus");
            go.GeometryFilter = ObjectType.InstanceReference;
            go.EnablePreSelect(true, true);
            go.GetMultiple(1, 1);
            if (go.CommandResult() != Result.Success)
                return go.CommandResult();

            var masterBlock = go.Object(0).Object() as InstanceObject;
            if (masterBlock == null)
            {
                RhinoApp.WriteLine("Fehler: Ausgewähltes Objekt ist kein Block.");
                return Result.Failure;
            }

            // Sammle alle Instanzen in der Hierarchie für die Vorschau
            var allComponents = new List<ExplosionComponent>();
            var visited = new HashSet<Guid>();
            
            // Sammle alle Komponenten für die Vorschau
            CollectAllComponents(masterBlock, masterBlock.InstanceXform, allComponents, visited, doc);
            
            if (allComponents.Count == 0)
            {
                RhinoApp.WriteLine("Keine Instanzen für Explosion gefunden.");
                return Result.Nothing;
            }

            // Berechne den Schwerpunkt des Masterblocks (das Explosionszentrum)
            var masterBbox = masterBlock.Geometry.GetBoundingBox(true);
            masterBbox.Transform(masterBlock.InstanceXform);
            var explosionCenter = masterBbox.Center;

            // Berechne den Schwerpunkt aller Komponenten (für relative Verschiebung)
            var componentsCentroid = CalculateCentroid(allComponents, masterBlock);

            // Zeige das UI-Fenster an
            var dialog = new ExplodeAssemblyDialog(doc, masterBlock, allComponents, explosionCenter, componentsCentroid);
            var result = dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);

            // Verarbeite das Ergebnis des Dialogs
            if (result)
            {
                RhinoApp.WriteLine("Dialog wurde mit 'Anwenden' geschlossen.");
                // Die Änderungen wurden bereits im Dialog angewendet
            }
            else
            {
                RhinoApp.WriteLine("Dialog wurde mit 'Abbrechen' geschlossen.");
                // Keine weiteren Aktionen erforderlich, da der Dialog bereits aufgeräumt hat
            }

            return Result.Success;
        }

        // Berechnet den Schwerpunkt aller Komponenten mit Gewichtung nach Volumen
        private Point3d CalculateCentroid(List<ExplosionComponent> components, InstanceObject masterBlock)
        {
            if (components.Count == 0)
            {
                // Fallback: Verwende den Schwerpunkt des Masterblocks
                var masterBbox = masterBlock.Geometry.GetBoundingBox(true);
                masterBbox.Transform(masterBlock.InstanceXform);
                return masterBbox.Center;
            }
            
            // Berechne den gewichteten Durchschnitt aller Zentren basierend auf dem Volumen
            Vector3d weightedSum = Vector3d.Zero;
            double totalVolume = 0.0;
            
            foreach (var comp in components)
            {
                // Berechne das Volumen der Komponente (approximiert durch Bounding Box)
                var bbox = comp.Instance.Geometry.GetBoundingBox(true);
                bbox.Transform(comp.FinalTransform);
                double volume = bbox.Volume;
                
                // Wenn das Volumen zu klein ist, verwende einen Mindestwert
                if (volume < 0.001)
                    volume = 0.001;
                
                // Addiere den gewichteten Schwerpunkt
                weightedSum += new Vector3d(comp.Centroid) * volume;
                totalVolume += volume;
            }
            
            // Berechne den gewichteten Durchschnitt
            if (totalVolume > 0.0)
                weightedSum /= totalVolume;
            else
                return components[0].Centroid; // Fallback
                
            return new Point3d(weightedSum);
        }

        // Sammelt nur die Komponenten der obersten Ebene
        private void CollectTopLevelComponents(InstanceObject instance, Transform parentTransform, 
            List<ExplosionComponent> components, RhinoDoc doc)
        {
            if (instance == null)
                return;
            
            // Die finale Transformation ist die Kombination aus der übergeordneten Transformation und der eigenen
            var finalTransform = instance.InstanceXform * parentTransform;
            
            // Bounding Box und Schwerpunkt berechnen
            var bbox = instance.Geometry.GetBoundingBox(true);
            var transformedBbox = bbox;
            transformedBbox.Transform(finalTransform);
            
            // Komponente erstellen
            var component = new ExplosionComponent
            {
                Instance = instance,
                Centroid = transformedBbox.Center,
                DefinitionIndex = instance.InstanceDefinition.Index,
                FinalTransform = finalTransform
            };
            
            components.Add(component);
        }

        // Sammelt alle Komponenten auf allen Ebenen
        private void CollectAllComponents(InstanceObject instance, Transform parentTransform, 
            List<ExplosionComponent> components, HashSet<Guid> visited, RhinoDoc doc)
        {
            if (instance == null)
                return;
            
            // Die finale Transformation ist die Kombination aus der eigenen und der übergeordneten Transformation
            var finalTransform = Transform.Multiply(instance.InstanceXform, parentTransform);
            
            // Prüfe, ob die Instanz Subblöcke hat
            var def = instance.InstanceDefinition;
            if (def == null)
                return;
            
                var subObjects = def.GetObjects();
                bool hasSubBlocks = false;
            
                foreach (var obj in subObjects)
                {
                    if (obj is InstanceObject)
                    {
                        hasSubBlocks = true;
                        break;
                    }
                }
            
            // Wenn die Instanz keine Subblöcke hat oder wir am Ende der Hierarchie sind,
            // fügen wir sie als Komponente hinzu
                if (!hasSubBlocks)
            {
                // Bounding Box und Schwerpunkt berechnen
                var bbox = instance.Geometry.GetBoundingBox(true);
                var transformedBbox = bbox;
                transformedBbox.Transform(finalTransform);
                
                // Komponente erstellen
                var component = new ExplosionComponent
                {
                    Instance = instance,
                    Centroid = transformedBbox.Center,
                    DefinitionIndex = instance.InstanceDefinition.Index,
                    FinalTransform = finalTransform,
                    OriginalTransform = finalTransform,
                    InstanceXform = instance.InstanceXform,
                    // Speichere die relative Position zum Masterblock - wird später in UpdatePreview gesetzt
                    RelativePosition = new Vector3d(0, 0, 0)
                };
                
                components.Add(component);
            }
            
            // Rekursiv alle Subinstanzen durchlaufen
            foreach (var obj in subObjects)
            {
                if (obj is InstanceObject subInstance)
                {
                    // WICHTIG: Wir geben die finale Transformation des aktuellen Blocks weiter
                    CollectAllComponents(subInstance, finalTransform, components, visited, doc);
                }
            }
        }

        // Sammelt alle Instanzen für die Löschung
        private void CollectAllInstancesForDeletion(InstanceObject instance, List<InstanceObject> instances, 
            HashSet<Guid> visited, RhinoDoc doc)
        {
            if (instance == null || visited.Contains(instance.Id))
            {
                return;
            }
            
            visited.Add(instance.Id);
            instances.Add(instance);
            
            var def = instance.InstanceDefinition;
            if (def == null)
            {
                return;
            }
            
            var subObjects = def.GetObjects();
            foreach (var obj in subObjects)
            {
                if (obj is InstanceObject subInstance)
                {
                    CollectAllInstancesForDeletion(subInstance, instances, visited, doc);
                }
            }
        }
    }

    // Dialog-Klasse für die Explosionssteuerung
    public class ExplodeAssemblyDialog : Dialog<bool>
    {
        private readonly RhinoDoc _doc;
        private readonly InstanceObject _masterBlock;
        private readonly List<ExplosionComponent> _components;
        private readonly Point3d _explosionCenter;
        private readonly Point3d _componentsCentroid;
        private readonly List<Guid> _newInstanceIds = new List<Guid>();
        private readonly List<Guid> _centroidMarkerIds = new List<Guid>();
        private readonly List<Guid> _connectionLineIds = new List<Guid>();
        
        // Konstante für die Achsenausrichtungs-Toleranz als Prozent der Gesamtgröße
        private const double AXIS_ALIGNMENT_PERCENT = 0.01; // 1% der Diagonale
        
        private double _explosionValue = 0.0;  // Standardwert ist 0 (keine Verschiebung)
        private int _explosionMode = 0;
        private double _explosionFactor = 1.0;
        private bool _preserveHierarchy = false;
        private bool _visualizeCentroids = false;
        private bool _visualizeConnectionLines = true;
        private bool _keepConnectionLines = false; // Neue Option: Verbindungslinien nach der Anwendung behalten
        private bool _autoAdjustCamera = true; // Neue Option: Kamera automatisch anpassen
        
        private readonly string[] _explosionModes = new string[] { "Zentrum", "Relativ", "Achsen" };
        
        // UI-Elemente
        private Slider _explosionSlider;
        private TextBox _explosionValueTextBox;
        private DropDown _modeDropDown;
        private Slider _factorSlider;
        private TextBox _factorTextBox;
        private CheckBox _hierarchyCheckBox;
        private CheckBox _centroidsCheckBox;
        private CheckBox _connectionLinesCheckBox;
        private ListBox _componentsList;
        private Button _resetViewButton;

        public ExplodeAssemblyDialog(RhinoDoc doc, InstanceObject masterBlock, List<ExplosionComponent> components, 
            Point3d explosionCenter, Point3d componentsCentroid)
        {
            _doc = doc;
            _masterBlock = masterBlock;
            _components = components;
            _explosionCenter = explosionCenter;
            _componentsCentroid = componentsCentroid;
            
            // Dialog-Eigenschaften
            Title = "ExplodeAssembly - Explosionssteuerung";
            Padding = new Eto.Drawing.Padding(10);
            Resizable = true;
            MinimumSize = new Eto.Drawing.Size(400, 500);
            
            // UI erstellen
            Content = CreateLayout();
            
            // Buttons erstellen
            DefaultButton = new Button { Text = "Anwenden" };
            DefaultButton.Click += (sender, e) => ApplyChanges();
            
            AbortButton = new Button { Text = "Abbrechen" };
            AbortButton.Click += (sender, e) => CancelChanges();
            
            // Buttons zum Dialog hinzufügen
            var buttonLayout = new TableLayout
            {
                Padding = new Eto.Drawing.Padding(10, 10, 10, 10),
                Spacing = new Eto.Drawing.Size(5, 5),
                Rows = { new TableRow(null, DefaultButton, AbortButton) }
            };
            
            // Haupt-Layout
            var mainLayout = new DynamicLayout();
            mainLayout.Add(Content);
            mainLayout.Add(buttonLayout);
            
            Content = mainLayout;
            
            // Erste Vorschau erstellen
            UpdatePreview();
        }

        private Control CreateLayout()
        {
            var layout = new DynamicLayout();
            layout.Spacing = new Eto.Drawing.Size(5, 10);
            
            // Explosionswert-Steuerung
            layout.Add(new Label { Text = "Explosionsstärke:" });
            
            _explosionSlider = new Slider
            {
                MinValue = 0,
                MaxValue = 100,
                Value = (int)_explosionValue,
                TickFrequency = 10
            };
            _explosionSlider.ValueChanged += (sender, e) => 
            {
                _explosionValue = _explosionSlider.Value;
                _explosionValueTextBox.Text = _explosionValue.ToString("F1");
                UpdatePreview();
            };
            
            _explosionValueTextBox = new TextBox
            {
                Text = _explosionValue.ToString("F1"),
                Width = 60
            };
            _explosionValueTextBox.TextChanged += (sender, e) =>
            {
                if (double.TryParse(_explosionValueTextBox.Text, out double value))
                {
                    if (value >= 0 && value <= 100)
                    {
                        _explosionValue = value;
                        _explosionSlider.Value = (int)value;
                        UpdatePreview();
                    }
                }
            };
            
            var explosionValueLayout = new TableLayout
            {
                Spacing = new Eto.Drawing.Size(5, 5),
                Rows = { new TableRow(_explosionSlider, _explosionValueTextBox) }
            };
            layout.Add(explosionValueLayout);
            
            // Explosionsmodus-Steuerung
            layout.Add(new Label { Text = "Explosionsmodus:" });
            
            _modeDropDown = new DropDown();
            foreach (var mode in _explosionModes)
            {
                _modeDropDown.Items.Add(mode);
            }
            _modeDropDown.SelectedIndex = _explosionMode;
            _modeDropDown.SelectedIndexChanged += (sender, e) =>
            {
                _explosionMode = _modeDropDown.SelectedIndex;
                UpdatePreview();
            };
            
            layout.Add(_modeDropDown);
            
            // Explosionsfaktor-Steuerung
            layout.Add(new Label { Text = "Explosionsfaktor:" });
            
            _factorSlider = new Slider
            {
                MinValue = 0,
                MaxValue = 50,
                Value = (int)(_explosionFactor * 10),
                TickFrequency = 5
            };
            _factorSlider.ValueChanged += (sender, e) =>
            {
                _explosionFactor = _factorSlider.Value / 10.0;
                _factorTextBox.Text = _explosionFactor.ToString("F1");
                UpdatePreview();
            };
            
            _factorTextBox = new TextBox
            {
                Text = _explosionFactor.ToString("F1"),
                Width = 60
            };
            _factorTextBox.TextChanged += (sender, e) =>
            {
                if (double.TryParse(_factorTextBox.Text, out double value))
                {
                    if (value >= 0.0 && value <= 5.0)
                    {
                        _explosionFactor = value;
                        _factorSlider.Value = (int)(value * 10);
                        UpdatePreview();
                    }
                }
            };
            
            var factorLayout = new TableLayout
            {
                Spacing = new Eto.Drawing.Size(5, 5),
                Rows = { new TableRow(_factorSlider, _factorTextBox) }
            };
            layout.Add(factorLayout);
            
            // Hinweis zum Explosionsfaktor
            layout.Add(new Label { 
                Text = "Hinweis: Faktor < 1.0: Nahe Objekte stärker verschieben\n" +
                       "         Faktor = 1.0: Lineare Verschiebung\n" +
                       "         Faktor > 1.0: Entfernte Objekte stärker verschieben",
                Font = new Eto.Drawing.Font(SystemFont.Default, 8)
            });
            
            // Hinweis zu den Explosionsmodi
            layout.Add(new Label { 
                Text = "Explosionsmodi:\n" +
                       "• Zentrum: Explosion vom Zentrum des Masterblocks\n" +
                       "• Relativ: Explosion vom Schwerpunkt aller Komponenten\n" +
                       "• Achsen: Explosion entlang der Hauptachsen (X, Y, Z)\n\n" +
                       "Hinweis: Komponenten, die auf einer Achse liegen, bleiben auf dieser Achse.\n" +
                       "Beispiel: Ein Teil bei X=0 bleibt immer bei X=0, unabhängig vom Modus.",
                Font = new Eto.Drawing.Font(SystemFont.Default, 8)
            });
            
            // Hierarchie-Checkbox
            _hierarchyCheckBox = new CheckBox
            {
                Text = "Hierarchie erhalten",
                Checked = _preserveHierarchy
            };
            _hierarchyCheckBox.CheckedChanged += (sender, e) =>
            {
                _preserveHierarchy = _hierarchyCheckBox.Checked ?? false;
                UpdatePreview();
            };
            
            layout.Add(_hierarchyCheckBox);
            
            // Schwerpunkte-Checkbox
            _centroidsCheckBox = new CheckBox
            {
                Text = "Schwerpunkte anzeigen",
                Checked = _visualizeCentroids
            };
            _centroidsCheckBox.CheckedChanged += (sender, e) =>
            {
                _visualizeCentroids = _centroidsCheckBox.Checked ?? false;
                UpdatePreview();
            };
            
            layout.Add(_centroidsCheckBox);
            
            // Verbindungslinien-Checkbox
            _connectionLinesCheckBox = new CheckBox
            {
                Text = "Verbindungslinien anzeigen",
                Checked = _visualizeConnectionLines
            };
            _connectionLinesCheckBox.CheckedChanged += (sender, e) =>
            {
                _visualizeConnectionLines = _connectionLinesCheckBox.Checked ?? false;
                UpdatePreview();
            };
            
            layout.Add(_connectionLinesCheckBox);
            
            // Neue Checkbox: Verbindungslinien behalten
            var keepConnectionLinesCheckBox = new CheckBox
            {
                Text = "Verbindungslinien beim Anwenden behalten",
                Checked = _keepConnectionLines
            };
            keepConnectionLinesCheckBox.CheckedChanged += (sender, e) =>
            {
                _keepConnectionLines = keepConnectionLinesCheckBox.Checked ?? false;
                RhinoApp.WriteLine($"Option 'Verbindungslinien behalten' wurde auf {_keepConnectionLines} gesetzt");
            };
            
            layout.Add(keepConnectionLinesCheckBox);
            
            // Neue Checkbox: Kamera automatisch anpassen
            var autoAdjustCameraCheckBox = new CheckBox
            {
                Text = "Kamera automatisch anpassen",
                Checked = _autoAdjustCamera
            };
            autoAdjustCameraCheckBox.CheckedChanged += (sender, e) =>
            {
                _autoAdjustCamera = autoAdjustCameraCheckBox.Checked ?? false;
                if (_autoAdjustCamera) {
                    UpdateViewForAllObjects();
                }
            };
            
            layout.Add(autoAdjustCameraCheckBox);
            
            // Komponenten-Liste
            layout.Add(new Label { Text = "Komponenten:" });
            
            _componentsList = new ListBox();
            _componentsList.Height = 150;
            
            // Fülle die Liste mit Komponenten
            foreach (var comp in _components)
            {
                _componentsList.Items.Add($"{comp.Instance.InstanceDefinition.Name}");
            }
            
            // Selektiere Komponente wenn auf ListBox-Element geklickt wird
            _componentsList.SelectedIndexChanged += (sender, e) =>
            {
                int selectedIndex = _componentsList.SelectedIndex;
                if (selectedIndex >= 0 && selectedIndex < _components.Count)
                {
                    // Hebe die Selektion aller anderen Objekte auf
                    _doc.Objects.UnselectAll();
                    
                    // Selektiere die ausgewählte Komponente
                    var component = _components[selectedIndex];
                    
                    // Finde die explodierte Version der Komponente
                    foreach (var id in _newInstanceIds)
                    {
                        var obj = _doc.Objects.FindId(id);
                        if (obj is InstanceObject instObj && 
                            instObj.InstanceDefinition.Index == component.DefinitionIndex)
                        {
                            // Selektiere diese Instanz
                            obj.Select(true);
                            
                            // Optional: Zum ausgewählten Objekt zoomen
                            var bbox = obj.Geometry.GetBoundingBox(true);
                            _doc.Views.ActiveView.ActiveViewport.ZoomBoundingBox(bbox);
                            
                            break;
                        }
                    }
                    
                    // Aktualisiere die Ansicht
                    _doc.Views.Redraw();
                }
            };
            
            layout.Add(_componentsList);
            
            // Button zum Zurücksetzen der Ansicht
            _resetViewButton = new Button { Text = "Ansicht auf Masterblock zurücksetzen" };
            _resetViewButton.Click += (sender, e) => ResetViewToMasterBlock();
            
            layout.Add(_resetViewButton);
            
            // Informationen
            var infoText = new Label
            {
                Text = $"Masterblock: {_masterBlock.InstanceDefinition.Name}\n" +
                       $"Anzahl Komponenten: {_components.Count}"
            };
            
            layout.Add(infoText);
            
            return layout;
        }

        private void UpdatePreview()
        {
            // Aufräumen der vorherigen Vorschau
            CleanupPreview();
            
            // Berechne das Explosionszentrum basierend auf dem Master-Block
            var masterBbox = _masterBlock.Geometry.GetBoundingBox(true);
            masterBbox.Transform(_masterBlock.InstanceXform);
            Point3d explodeCenter = masterBbox.Center;
            
            // Erstelle einen Marker-Block für die Centroide, falls er noch nicht existiert
            int markerBlockIndex = CreateCentroidMarkerBlock();
            
            // Visualisiere Schwerpunkte, wenn gewünscht
            if (_visualizeCentroids)
            {
                VisualizeCentroids(explodeCenter, markerBlockIndex);
            }
            
            // Bestimme die zu verwendenden Komponenten basierend auf der Hierarchie-Einstellung
            var componentsToUse = GetComponentsToUse();
            
            // Wenn keine Komponenten gefunden wurden, beende die Methode
            if (componentsToUse.Count == 0)
                return;
            
            // Berechne den Explosionsabstand basierend auf dem Explosionswert und der Master-Block-Größe
            double maxDiagonal = masterBbox.Diagonal.Length;
            double explosionDistance = _explosionValue / 100.0 * maxDiagonal;
            
            // Wenn keine Explosion stattfindet, erstelle eine exakte Kopie des Masterblocks
            if (Math.Abs(explosionDistance) < 1e-10)
            {
                Guid masterCopyId = _doc.Objects.AddInstanceObject(
                    _masterBlock.InstanceDefinition.Index, 
                    new Transform(_masterBlock.InstanceXform));
            
                if (masterCopyId != Guid.Empty)
                {
                    _newInstanceIds.Add(masterCopyId);
                }
                
                _doc.Views.Redraw();
                return;
            }
            
            // -------------------------------------------------------------------------
            // NEUER ANSATZ: SYMMETRIEBASIERTE EXPLOSION
            // -------------------------------------------------------------------------
            
            // 1. Gruppiere Komponenten nach Symmetrieachse relativ zum Explosionszentrum
            var symmetryGroups = GroupComponentsBySymmetry(componentsToUse, explodeCenter);
            
            // 2. Explodiere Komponenten nach ihrer Symmetriegruppe
            Dictionary<ExplosionComponent, Guid> explodedComponents = new Dictionary<ExplosionComponent, Guid>();
            
            // Der gewählte Explosionsmodus
            string explosionMode = _explosionModes[_explosionMode];
            
            // Bestimme das Referenzzentrum basierend auf dem Modus
            Point3d referenceCenter = explosionMode == "Relativ" ? _componentsCentroid : explodeCenter;
            
            // Protokolliere die Zentren für Debugging
            RhinoApp.WriteLine($"Masterblock-Zentrum: {explodeCenter.X}, {explodeCenter.Y}, {explodeCenter.Z}");
            RhinoApp.WriteLine($"Komponenten-Zentrum: {_componentsCentroid.X}, {_componentsCentroid.Y}, {_componentsCentroid.Z}");
            RhinoApp.WriteLine($"Referenz-Zentrum: {referenceCenter.X}, {referenceCenter.Y}, {referenceCenter.Z}");
            RhinoApp.WriteLine($"Explosionsmodus: {explosionMode}");
            
            // Für jede Komponente den Explosionsvektor berechnen und die Komponente explodieren
            foreach (var comp in componentsToUse)
            {
                // Berechne Explosionsrichtung und -distanz
                Vector3d explosionVector = CalculateExplosionVector(
                    comp, 
                    referenceCenter, 
                    explosionMode, 
                    symmetryGroups,
                    maxDiagonal,
                    explosionDistance);
                
                // Debug: Visualisiere den Explosionsvektor
                if (_visualizeConnectionLines) 
                {
                    Vector3d dirVector = new Vector3d(explosionVector);
                    dirVector.Unitize();
                    
                    // Wir zeichnen die Linien erst in VisualizeExplodedComponents,
                    // nachdem wir die tatsächlichen Zentroide der explodierten Komponenten kennen
                }
                
                // Verwende die Original-Transformation als Basis für die explodierte Transformation
                Transform explodedTransform = new Transform(comp.OriginalTransform);
                
                // Extrahiere die Position aus der Transformation
                Point3d originalPosition = new Point3d(
                    explodedTransform[0, 3],
                    explodedTransform[1, 3],
                    explodedTransform[2, 3]
                );
                
                // Berechne die neue Position durch Verschiebung in Explosionsrichtung
                Point3d explodedPosition = originalPosition + explosionVector;
                
                // Setze die neue Position in die Transformation
                explodedTransform[0, 3] = explodedPosition.X;
                explodedTransform[1, 3] = explodedPosition.Y;
                explodedTransform[2, 3] = explodedPosition.Z;
                
                // Erstelle die explodierte Instanz
                Guid explodedInstanceId = _doc.Objects.AddInstanceObject(comp.DefinitionIndex, explodedTransform);
                
                if (explodedInstanceId != Guid.Empty)
                {
                    _newInstanceIds.Add(explodedInstanceId);
                    explodedComponents.Add(comp, explodedInstanceId);
                }
            }
            
            // Visualisiere Verbindungslinien und Centroide
            VisualizeExplodedComponents(explodedComponents, explodeCenter, markerBlockIndex);
            
            // Aktualisiere die Ansicht (mit optionaler automatischer Anpassung)
            _doc.Views.Redraw();
            
            // Wenn die Option aktiviert ist, passe die Kamera an alle Objekte an
            if (_autoAdjustCamera) {
                UpdateViewForAllObjects();
            }
        }
        
        // Neue Methode zum Berechnen des Explosionsvektors für eine Komponente
        private Vector3d CalculateExplosionVector(
            ExplosionComponent comp, 
            Point3d referenceCenter, 
            string explosionMode, 
            Dictionary<string, List<ExplosionComponent>> symmetryGroups,
            double maxDiagonal,
            double explosionDistance)
        {
            // 1. Bestimme den Richtungsvektor vom Bezugszentrum zur Komponente
            Vector3d baseDirection = comp.Centroid - referenceCenter;
            
            // Wenn der Vektor zu klein ist, generiere eine zufällige Richtung
            if (baseDirection.Length < 1e-10)
            {
                int hash = comp.Instance.Id.GetHashCode();
                var random = new Random(hash);
                baseDirection = new Vector3d(
                    random.NextDouble() * 2 - 1,
                    random.NextDouble() * 2 - 1,
                    random.NextDouble() * 2 - 1
                );
            }
            
            Vector3d explodeDirection;
            
            // 2. Bestimme die Explosionsrichtung basierend auf dem Modus und der Symmetriegruppe
            switch (explosionMode)
            {
                case "Achsen":
                    // Im Achsenmodus: explodieren entlang der stärksten Achsenrichtung
                    explodeDirection = GetDominantAxisDirection(baseDirection);
                    break;
                            
                case "Zentrum":
                case "Relativ":
                default:
                    // In diesen Modi müssen wir die Achsenausrichtung erhalten
                    // Prüfe, ob die Komponente auf oder nahe einer Hauptachse liegt
                    // Wenn Komponente nahe einer Achse liegt (X=0, Y=0 oder Z=0), dann nur in Achsenrichtung verschieben
                    
                    double axisAlignmentTolerance = AXIS_ALIGNMENT_PERCENT * maxDiagonal; // Toleranz relativ zur Gesamtgröße
                    bool alignedToXAxis = Math.Abs(comp.Centroid.X - referenceCenter.X) < axisAlignmentTolerance;
                    bool alignedToYAxis = Math.Abs(comp.Centroid.Y - referenceCenter.Y) < axisAlignmentTolerance;
                    bool alignedToZAxis = Math.Abs(comp.Centroid.Z - referenceCenter.Z) < axisAlignmentTolerance;
                    
                    if (alignedToXAxis && alignedToYAxis) {
                        // Liegt auf der Z-Achse - nur in Z-Richtung verschieben
                        explodeDirection = new Vector3d(0, 0, Math.Sign(baseDirection.Z));
                    } 
                    else if (alignedToXAxis && alignedToZAxis) {
                        // Liegt auf der Y-Achse - nur in Y-Richtung verschieben
                        explodeDirection = new Vector3d(0, Math.Sign(baseDirection.Y), 0);
                    }
                    else if (alignedToYAxis && alignedToZAxis) {
                        // Liegt auf der X-Achse - nur in X-Richtung verschieben
                        explodeDirection = new Vector3d(Math.Sign(baseDirection.X), 0, 0);
                    }
                    else if (alignedToXAxis) {
                        // Liegt in der YZ-Ebene - keine X-Komponente in der Verschiebung
                        explodeDirection = new Vector3d(0, baseDirection.Y, baseDirection.Z);
                        explodeDirection.Unitize();
                    }
                    else if (alignedToYAxis) {
                        // Liegt in der XZ-Ebene - keine Y-Komponente in der Verschiebung
                        explodeDirection = new Vector3d(baseDirection.X, 0, baseDirection.Z);
                        explodeDirection.Unitize();
                    }
                    else if (alignedToZAxis) {
                        // Liegt in der XY-Ebene - keine Z-Komponente in der Verschiebung
                        explodeDirection = new Vector3d(baseDirection.X, baseDirection.Y, 0);
                        explodeDirection.Unitize();
                    }
                    else {
                        // Keine spezielle Ausrichtung - verwende normalisierte Richtung
                        explodeDirection = baseDirection;
                        explodeDirection.Unitize();
                    }
                    break;
            }
            
            // 3. Berechne den Explosionsversatz basierend auf der Distanz zum Zentrum und dem Faktor
            double distanceFromCenter = baseDirection.Length;
            double normalizedDistance = maxDiagonal > 0 ? distanceFromCenter / maxDiagonal : 0;
            normalizedDistance = Math.Max(0.001, normalizedDistance);
            
            double explosionDisplacement;
            
            if (_explosionFactor < 1.0)
            {
                explosionDisplacement = explosionDistance * Math.Pow(normalizedDistance, 1.0 / (1.0 + _explosionFactor));
            }
            else if (_explosionFactor > 1.0)
            {
                explosionDisplacement = explosionDistance * Math.Pow(normalizedDistance, _explosionFactor);
            }
            else
            {
                explosionDisplacement = explosionDistance * normalizedDistance;
            }
            
            // 4. Berechne den finalen Explosionsvektor
            return explodeDirection * explosionDisplacement;
        }
        
        // Methode zur Ermittlung der dominanten Achsenrichtung
        private Vector3d GetDominantAxisDirection(Vector3d direction)
        {
            double absX = Math.Abs(direction.X);
            double absY = Math.Abs(direction.Y);
            double absZ = Math.Abs(direction.Z);
            
            // Bestimme die dominante Achse
            if (absX >= absY && absX >= absZ)
            {
                return new Vector3d(Math.Sign(direction.X), 0, 0);
            }
            else if (absY >= absX && absY >= absZ)
            {
                return new Vector3d(0, Math.Sign(direction.Y), 0);
            }
            else
            {
                return new Vector3d(0, 0, Math.Sign(direction.Z));
            }
        }
        
        // Methode zur Gruppierung von Komponenten nach Symmetrieachsen
        private Dictionary<string, List<ExplosionComponent>> GroupComponentsBySymmetry(
            List<ExplosionComponent> components, 
            Point3d center)
        {
            var groups = new Dictionary<string, List<ExplosionComponent>>();
            
            // Für jede Komponente
            foreach (var comp in components)
            {
                // Bestimme die Position relativ zum Zentrum
                Vector3d relPos = comp.Centroid - center;
                
                // Runde die Koordinaten auf eine definierte Genauigkeit für die Symmetrieprüfung
                const int precision = 3; // 3 Dezimalstellen
                double x = Math.Round(relPos.X, precision);
                double y = Math.Round(relPos.Y, precision);
                double z = Math.Round(relPos.Z, precision);
                
                // Erzeuge verschiedene Schlüssel für die Symmetrieprüfung
                
                // 1. X-Achsensymmetrie: nur das Vorzeichen von X unterscheidet sich
                string xAxisKey = String.Format("X:{0:F" + precision + "}|Y:{1:F" + precision + "}|Z:{2:F" + precision + "}", 
                    Math.Abs(x), y, z);
                
                // 2. Y-Achsensymmetrie: nur das Vorzeichen von Y unterscheidet sich
                string yAxisKey = String.Format("X:{0:F" + precision + "}|Y:{1:F" + precision + "}|Z:{2:F" + precision + "}", 
                    x, Math.Abs(y), z);
                
                // 3. Z-Achsensymmetrie: nur das Vorzeichen von Z unterscheidet sich
                string zAxisKey = String.Format("X:{0:F" + precision + "}|Y:{1:F" + precision + "}|Z:{2:F" + precision + "}", 
                    x, y, Math.Abs(z));
                
                // Bestimme auch die Hauptachse für die Komponente
                string mainAxis = GetMainAxisKey(relPos);
                
                // Füge die Komponente zu den entsprechenden Gruppen hinzu
                if (!groups.ContainsKey(xAxisKey)) groups[xAxisKey] = new List<ExplosionComponent>();
                if (!groups.ContainsKey(yAxisKey)) groups[yAxisKey] = new List<ExplosionComponent>();
                if (!groups.ContainsKey(zAxisKey)) groups[zAxisKey] = new List<ExplosionComponent>();
                if (!groups.ContainsKey(mainAxis)) groups[mainAxis] = new List<ExplosionComponent>();
                
                groups[xAxisKey].Add(comp);
                groups[yAxisKey].Add(comp);
                groups[zAxisKey].Add(comp);
                groups[mainAxis].Add(comp);
            }
            
            return groups;
        }
        
        // Bestimmt den Hauptachsenschlüssel einer Komponente
        private string GetMainAxisKey(Vector3d direction)
        {
            double absX = Math.Abs(direction.X);
            double absY = Math.Abs(direction.Y);
            double absZ = Math.Abs(direction.Z);
            
            // Bestimme, ob die Komponente nahe einer Hauptachse liegt
            const double axisAlignmentTolerance = 0.05;
            
            if (absY < axisAlignmentTolerance && absZ < axisAlignmentTolerance)
            {
                return "MainAxis:X:" + Math.Sign(direction.X);
            }
            else if (absX < axisAlignmentTolerance && absZ < axisAlignmentTolerance)
            {
                return "MainAxis:Y:" + Math.Sign(direction.Y);
            }
            else if (absX < axisAlignmentTolerance && absY < axisAlignmentTolerance)
            {
                return "MainAxis:Z:" + Math.Sign(direction.Z);
            }
            
            // Wenn keine klare Hauptachse erkannt wird, verwende die dominante Achse
            if (absX >= absY && absX >= absZ)
            {
                return "DominantAxis:X:" + Math.Sign(direction.X);
            }
            else if (absY >= absX && absY >= absZ)
            {
                return "DominantAxis:Y:" + Math.Sign(direction.Y);
                    }
                    else
                    {
                return "DominantAxis:Z:" + Math.Sign(direction.Z);
            }
        }
        
        // Methode zur Visualisierung der explodierten Komponenten
        private void VisualizeExplodedComponents(
            Dictionary<ExplosionComponent, Guid> explodedComponents,
            Point3d explodeCenter,
            int markerBlockIndex)
        {
            // Der gewählte Explosionsmodus
            string explosionMode = _explosionModes[_explosionMode];
            
            // Bestimme das Referenzzentrum basierend auf dem Modus
            Point3d referenceCenter = explosionMode == "Relativ" ? _componentsCentroid : explodeCenter;
            
            // Für jede explodierte Komponente
            foreach (var pair in explodedComponents)
            {
                var comp = pair.Key;
                var explodedId = pair.Value;
                
                // Lade das explodierte Objekt
                var explodedObj = _doc.Objects.FindId(explodedId);
                if (explodedObj == null || !explodedObj.IsValid)
                    continue;
                
                // Berechne den tatsächlichen Centroid des explodierten Objekts
                BoundingBox explodedBBox = explodedObj.Geometry.GetBoundingBox(true);
                Point3d actualCentroid = explodedBBox.Center;
                
                // Erstelle einen roten Marker am tatsächlichen Centroid mit dem vordefinierten Block
                if (_visualizeCentroids)
                {
                    Transform markerTransform = Transform.Translation(actualCentroid.X, actualCentroid.Y, actualCentroid.Z);
                    var markerAttr = new ObjectAttributes();
                    markerAttr.ColorSource = ObjectColorSource.ColorFromObject;
                    markerAttr.ObjectColor = System.Drawing.Color.Red;
                    
                    var centroidId = _doc.Objects.AddInstanceObject(markerBlockIndex, markerTransform, markerAttr);
                    _centroidMarkerIds.Add(centroidId); // Wichtig: Zu _centroidMarkerIds hinzufügen, nicht zu _newInstanceIds
                }
                
                // Erstelle eine gelbe Linie vom Explosionszentrum zum tatsächlichen Centroid (wenn gewünscht)
                if (_visualizeConnectionLines)
                {
                    var line = new Line(referenceCenter, actualCentroid);
                    var lineAttr = new ObjectAttributes();
                    lineAttr.ColorSource = ObjectColorSource.ColorFromObject;
                    lineAttr.ObjectColor = System.Drawing.Color.Yellow;
                    lineAttr.PlotWeight = 5; // Dickere Linie für bessere Sichtbarkeit
                    lineAttr.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromObject;
                    
                    // Zeichne die Linie und speichere die ID
                    var lineId = _doc.Objects.AddLine(line, lineAttr);
                    _connectionLineIds.Add(lineId); // Nur zu _connectionLineIds hinzufügen
                }
            }
        }

        // Erstellt einen Block für den Centroid-Marker, wenn er noch nicht existiert
        private int CreateCentroidMarkerBlock()
        {
            string blockName = "CentroidMarker";
            
            // Prüfe, ob der Block bereits existiert
            for (int i = 0; i < _doc.InstanceDefinitions.Count; i++)
            {
                if (_doc.InstanceDefinitions[i].Name == blockName)
                    return i;
            }
            
            // Erstelle eine Kugel als Geometrie für den Block
            var sphere = new Sphere(Point3d.Origin, 3.0);
            var sphereMesh = Mesh.CreateFromSphere(sphere, 8, 6);
            
            // Erstelle das Attribut-Objekt
            var attributes = new ObjectAttributes();
            attributes.ColorSource = ObjectColorSource.ColorFromObject;
            attributes.ObjectColor = System.Drawing.Color.Red;
            
            // Listen für Geometrie und Attribute erstellen
            var geometryList = new List<GeometryBase> { sphereMesh };
            var attributesList = new List<ObjectAttributes> { attributes };
            
            // Erstelle den Block mit der richtigen API-Signatur
            int blockIndex = _doc.InstanceDefinitions.Add(
                blockName, 
                "Marker für Zentrum", 
                Point3d.Origin, 
                geometryList, 
                attributesList);
            
            return blockIndex;
        }

        // Visualisiert die Zentrumspunkte
        private void VisualizeCentroids(Point3d masterCenter, int markerBlockIndex)
        {
            if (markerBlockIndex < 0)
                return;
            
            // Visualisiere das Explosionszentrum mit dem Block
            Transform centerTransform = Transform.Translation(masterCenter.X, masterCenter.Y, masterCenter.Z);
            var centerAttr = new ObjectAttributes();
            centerAttr.ColorSource = ObjectColorSource.ColorFromObject;
            centerAttr.ObjectColor = System.Drawing.Color.Red;
            
            var centerId = _doc.Objects.AddInstanceObject(markerBlockIndex, centerTransform, centerAttr);
            _centroidMarkerIds.Add(centerId);
            
            // Visualisiere den Komponenten-Schwerpunkt mit dem Block
            Transform compCenterTransform = Transform.Translation(_componentsCentroid.X, _componentsCentroid.Y, _componentsCentroid.Z);
            var compCenterAttr = new ObjectAttributes();
            compCenterAttr.ColorSource = ObjectColorSource.ColorFromObject;
            compCenterAttr.ObjectColor = System.Drawing.Color.Blue;
            
            var compCenterId = _doc.Objects.AddInstanceObject(markerBlockIndex, compCenterTransform, compCenterAttr);
            _centroidMarkerIds.Add(compCenterId);
            
            // Visualisiere die Schwerpunkte der einzelnen Komponenten mit dem Block
            foreach (var comp in _components)
            {
                Transform compTransform = Transform.Translation(comp.Centroid.X, comp.Centroid.Y, comp.Centroid.Z);
                var compAttr = new ObjectAttributes();
                compAttr.ColorSource = ObjectColorSource.ColorFromObject;
                compAttr.ObjectColor = System.Drawing.Color.Green;
                
                var id = _doc.Objects.AddInstanceObject(markerBlockIndex, compTransform, compAttr);
                _centroidMarkerIds.Add(id);
            }
        }

        // Filtert die zu verwendenden Komponenten basierend auf den Hierarchieeinstellungen
        private List<ExplosionComponent> GetComponentsToUse()
        {
            if (!_preserveHierarchy)
                return _components;
            
            var componentsToUse = new List<ExplosionComponent>();
                var visited = new HashSet<Guid>();
                
                foreach (var comp in _components)
                {
                    if (!visited.Contains(comp.Instance.Id))
                    {
                        componentsToUse.Add(comp);
                        visited.Add(comp.Instance.Id);
                }
            }
            
            return componentsToUse;
        }

        private void CleanupPreview()
        {
            // Lösche alle vorherigen Vorschau-Instanzen
            foreach (var id in _newInstanceIds)
            {
                _doc.Objects.Delete(id, true);
            }
            _newInstanceIds.Clear();
            
            // Lösche alle Schwerpunktmarker
            foreach (var id in _centroidMarkerIds)
            {
                _doc.Objects.Delete(id, true);
            }
            _centroidMarkerIds.Clear();
            
            // Lösche alle Verbindungslinien
            foreach (var id in _connectionLineIds)
            {
                _doc.Objects.Delete(id, true);
            }
            _connectionLineIds.Clear();
            
            _doc.Views.Redraw();
        }

        private void ApplyChanges()
        {
            // Bestätige die Änderungen und beende
            RhinoApp.WriteLine("Änderungen übernommen.");
            RhinoApp.WriteLine($"Option 'Verbindungslinien behalten' ist {(_keepConnectionLines ? "aktiviert" : "deaktiviert")}");
            
            // Sammle alle Daten, die wir für dauerhafte Verbindungslinien benötigen
            List<Line> linesToKeep = new List<Line>();
            string explosionMode = _explosionModes[_explosionMode];
            Point3d referenceCenter = explosionMode == "Relativ" ? _componentsCentroid : _explosionCenter;
            
            // Sammle die tatsächlichen Linien, falls wir sie behalten möchten
            if (_keepConnectionLines)
            {
                // Sammle die Daten für die permanenten Linien
                foreach (var id in _newInstanceIds)
                {
                    var obj = _doc.Objects.FindId(id);
                    if (obj != null && obj.IsValid)
                    {
                        BoundingBox bbox = obj.Geometry.GetBoundingBox(true);
                        Point3d actualCentroid = bbox.Center;
                        
                        // Erstelle eine Linie vom Referenzzentrum zum tatsächlichen Zentroid
                        Line line = new Line(referenceCenter, actualCentroid);
                        linesToKeep.Add(line);
                    }
                }
            }
            
            // Lösche den Masterblock und seine Subblöcke
            if (_preserveHierarchy)
            {
                // Nur den Masterblock löschen
                _doc.Objects.Delete(_masterBlock, true);
            }
            else
            {
                // Alle Instanzen in der Hierarchie löschen
                var allInstancesToDelete = new List<InstanceObject>();
                var visitedForDeletion = new HashSet<Guid>();
                CollectAllInstancesForDeletion(_masterBlock, allInstancesToDelete, visitedForDeletion, _doc);
                
                foreach (var inst in allInstancesToDelete)
                {
                    if (_doc.Objects.FindId(inst.Id).IsValid)
                    {
                        _doc.Objects.Delete(inst, true);
                    }
                }
            }
            
            // Lösche die Schwerpunktmarker
            foreach (var id in _centroidMarkerIds)
            {
                _doc.Objects.Delete(id, true);
            }
            _centroidMarkerIds.Clear();
            
            // Lösche die temporären Verbindungslinien (wir werden permanente erstellen, falls gewünscht)
            foreach (var id in _connectionLineIds)
            {
                _doc.Objects.Delete(id, true);
            }
            _connectionLineIds.Clear();
            
            // Erstelle permanente Verbindungslinien, falls gewünscht
            if (_keepConnectionLines && linesToKeep.Count > 0)
            {
                RhinoApp.WriteLine($"Erstelle {linesToKeep.Count} permanente Verbindungslinien");
                
                foreach (var line in linesToKeep)
                {
                    var lineAttr = new ObjectAttributes();
                    lineAttr.ColorSource = ObjectColorSource.ColorFromObject;
                    lineAttr.ObjectColor = System.Drawing.Color.Yellow;
                    lineAttr.PlotWeight = 5; // Dickere Linie für bessere Sichtbarkeit
                    lineAttr.PlotWeightSource = ObjectPlotWeightSource.PlotWeightFromObject;
                    lineAttr.Name = "Verbindungslinie (Explosion)"; // Benennt die Linien für einfache Identifikation
                    
                    // Erstelle eine PERMANENTE Linie in der Dokumentation
                    var permanentLineId = _doc.Objects.AddLine(line, lineAttr);
                    
                    if (permanentLineId != Guid.Empty)
                    {
                        RhinoApp.WriteLine($"Permanente Linie {permanentLineId} erstellt");
                    }
                }
            }
            
            // WICHTIG: Wir löschen die _newInstanceIds Liste, aber NICHT die Objekte selbst,
            // damit die neuen Instanzen erhalten bleiben
            _newInstanceIds.Clear();
            
            _doc.Views.Redraw();
            
            // Schließe den Dialog
            Close(true);
        }

        private void CancelChanges()
        {
            // Lösche alle Vorschau-Objekte
            CleanupPreview();
            
            // Schließe den Dialog
            Close(false);
        }

        private void CollectAllInstancesForDeletion(InstanceObject instance, List<InstanceObject> instances, 
            HashSet<Guid> visited, RhinoDoc doc)
        {
            if (instance == null || visited.Contains(instance.Id))
            {
                return;
            }
            
            visited.Add(instance.Id);
            instances.Add(instance);

            var def = instance.InstanceDefinition;
            if (def == null)
            {
                return;
            }
            
            var subObjects = def.GetObjects();
            foreach (var obj in subObjects)
            {
                if (obj is InstanceObject subInstance)
                {
                    CollectAllInstancesForDeletion(subInstance, instances, visited, doc);
                }
            }
        }

        // Neue Methode zum Zurücksetzen der Ansicht auf den Masterblock
        private void ResetViewToMasterBlock()
        {
            // Berechne die Bounding Box des Masterblocks
            var masterBbox = _masterBlock.Geometry.GetBoundingBox(true);
            masterBbox.Transform(_masterBlock.InstanceXform);
            
            // Füge etwas Platz hinzu, damit der Masterblock vollständig sichtbar ist
            masterBbox.Inflate(masterBbox.Diagonal.Length * 0.1);
            
            // Zoome zur Bounding Box
            _doc.Views.ActiveView.ActiveViewport.ZoomBoundingBox(masterBbox);
            _doc.Views.Redraw();
        }

        // Neue Methode, um die Ansicht auf alle Objekte einzustellen (inkl. Verbindungslinien)
        private void UpdateViewForAllObjects()
        {
            // Sammle alle Objekte, die wir betrachten wollen
            var allObjectsToView = new List<Guid>();
            allObjectsToView.AddRange(_newInstanceIds);
            allObjectsToView.AddRange(_connectionLineIds);
            
            if (allObjectsToView.Count == 0)
                return;
                
            // Erstelle eine gemeinsame BoundingBox für alle Objekte
            BoundingBox combinedBox = BoundingBox.Empty;
            
            foreach (var id in allObjectsToView)
            {
                var obj = _doc.Objects.FindId(id);
                if (obj == null || !obj.IsValid)
                    continue;
                    
                BoundingBox objBox = obj.Geometry.GetBoundingBox(true);
                combinedBox.Union(objBox);
            }
            
            if (!combinedBox.IsValid)
                return;
                
            // Füge etwas Platz hinzu, damit alles gut sichtbar ist
            combinedBox.Inflate(combinedBox.Diagonal.Length * 0.1);
            
            // Zoome zur Bounding Box
            _doc.Views.ActiveView.ActiveViewport.ZoomBoundingBox(combinedBox);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Aufräumen beim Schließen
            CleanupPreview();
            base.OnClosing(e);
        }
    }
    public class ExplosionComponent
    {
        public InstanceObject Instance { get; set; }
        public Point3d Centroid { get; set; }
        public int DefinitionIndex { get; set; }
        public Transform FinalTransform { get; set; }  // Eigenschaft für die finale Transformation
        public Transform OriginalTransform { get; set; } // Speichert die ursprüngliche Transformation
        public Transform InstanceXform { get; set; } // Speichert die Transformation der Instanz im Definitionsraum
        public Vector3d RelativePosition { get; set; } // Speichert die relative Position zum Masterblock-Zentrum
    }
}