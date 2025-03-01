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
        
        private double _explosionValue = 0.0;  // Standardwert ist 0 (keine Verschiebung)
        private int _explosionMode = 0;
        private double _explosionFactor = 1.0;
        private bool _preserveHierarchy = false;
        private bool _visualizeCentroids = false;
        
        private readonly string[] _explosionModes = new string[] { "Zentrum", "Relativ", "Achsen" };
        
        // UI-Elemente
        private Slider _explosionSlider;
        private TextBox _explosionValueTextBox;
        private DropDown _modeDropDown;
        private Slider _factorSlider;
        private TextBox _factorTextBox;
        private CheckBox _hierarchyCheckBox;
        private CheckBox _centroidsCheckBox;
        private ListBox _componentsList;

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
                       "• Achsen: Explosion entlang der Hauptachsen (X, Y, Z)",
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
            
            // Komponenten-Liste
            layout.Add(new Label { Text = "Komponenten:" });
            
            _componentsList = new ListBox();
            _componentsList.Height = 150;
            
            // Fülle die Liste mit Komponenten
            foreach (var comp in _components)
            {
                _componentsList.Items.Add($"{comp.Instance.InstanceDefinition.Name}");
            }
            
            layout.Add(_componentsList);
            
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
            
            // Visualisiere Schwerpunkte, wenn gewünscht
            if (_visualizeCentroids)
            {
                VisualizeCentroids(explodeCenter);
            }
            
            // Bestimme die zu verwendenden Komponenten basierend auf der Hierarchie-Einstellung
            var componentsToUse = GetComponentsToUse();
            
            // Wenn keine Komponenten gefunden wurden, beende die Methode
            if (componentsToUse.Count == 0)
                return;
            
            // Berechne den Explosionsabstand basierend auf dem Explosionswert und der Master-Block-Größe
            double maxDiagonal = masterBbox.Diagonal.Length;
            double explosionDistance = _explosionValue / 100.0 * maxDiagonal;
            
            // -------------------------------------------------------------------------
            // KOMPLETT NEUER ANSATZ: GEOMETRIEBASIERTE EXPLOSION
            // -------------------------------------------------------------------------
            
            // 1. Erstelle eine exakte Kopie des Master-Blocks, wenn die Explosionsdistanz 0 ist
            if (Math.Abs(explosionDistance) < 1e-10)
            {
                // Erstelle eine exakte Kopie des Master-Blocks mit identischer Transformation
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
            
            // 2. Erstelle für jede Komponente eine eigene explodierte Instanz
            // Sortiere die Komponenten nach ihrer Entfernung vom Explosionszentrum
            var sortedComponents = new List<ExplosionComponent>(componentsToUse);
            sortedComponents.Sort((a, b) => 
            {
                double distA = (a.Centroid - explodeCenter).Length;
                double distB = (b.Centroid - explodeCenter).Length;
                return distA.CompareTo(distB); // Aufsteigend sortieren
            });
            
            // Der gewählte Explosionsmodus
            string explosionMode = _explosionModes[_explosionMode];
            
            // Explosion "Radiusfaktor": bestimmt, wie weit die Komponenten vom Zentrum wegbewegt werden
            double radiusFactor = _explosionFactor > 0 ? _explosionFactor : 1.0;
            
            // Erstelle eine Liste zur Vermeidung von Kollisionen
            var occupiedPositions = new List<BoundingBox>();
            
            // Erstelle für jede Komponente einen eigenen Block mit explodierter Position
            foreach (var comp in sortedComponents)
            {
                // Die Komponentendefinition und -geometrie
                var componentDef = comp.Instance.InstanceDefinition;
                
                // Berechne die Explosionsrichtung basierend auf dem Explosionsmodus und Explosionszentrum
                Vector3d explodeDirection = CalculateExplosionVector(comp, explodeCenter, explosionMode);
                
                // Berechne die Entfernung dieser Komponente vom Explosionszentrum
                double distanceFromCenter = (comp.Centroid - explodeCenter).Length;
                
                // Normalisiere die Distanz für eine einheitliche Explosion
                double normalizedDistance = maxDiagonal > 0 ? distanceFromCenter / maxDiagonal : 0;
                normalizedDistance = Math.Max(0.001, normalizedDistance); // Verhindere Division durch 0
                
                // Berechne den Explosionsversatz basierend auf der normalisierten Distanz und dem Explosionsfaktor
                double explosionDisplacement;
                
                if (_explosionFactor < 1.0)
                {
                    // Für Faktoren < 1: Stärkere Verschiebung für nahe Objekte
                    explosionDisplacement = explosionDistance * Math.Pow(normalizedDistance, 1.0 / (1.0 + _explosionFactor));
                }
                else if (_explosionFactor > 1.0)
                {
                    // Für Faktoren > 1: Stärkere Verschiebung für entfernte Objekte
                    explosionDisplacement = explosionDistance * Math.Pow(normalizedDistance, _explosionFactor);
                }
                else
                {
                    // Für Faktor = 1: Lineare Verschiebung
                    explosionDisplacement = explosionDistance * normalizedDistance;
                }
                
                // Verwende die Original-Transformation als Basis
                Transform explodedTransform = new Transform(comp.OriginalTransform);
                
                // Extrahiere die Position aus der Transformation
                Point3d originalPosition = new Point3d(
                    explodedTransform[0, 3],
                    explodedTransform[1, 3],
                    explodedTransform[2, 3]
                );
                
                // Berechne die neue Position durch Verschiebung in Explosionsrichtung
                Point3d explodedPosition = originalPosition + explodeDirection * explosionDisplacement;
                
                // Setze die neue Position in die Transformation
                explodedTransform[0, 3] = explodedPosition.X;
                explodedTransform[1, 3] = explodedPosition.Y;
                explodedTransform[2, 3] = explodedPosition.Z;
                
                // Erstelle eine neue Instanz mit der explodierten Transformation
                Guid explodedInstanceId = _doc.Objects.AddInstanceObject(comp.DefinitionIndex, explodedTransform);
                
                if (explodedInstanceId != Guid.Empty)
                {
                    _newInstanceIds.Add(explodedInstanceId);
                    
                    // Optional: Verbindungslinien zwischen Originalposition und explodierter Position zeichnen
                    if (explosionDisplacement > 0.1) // Nur wenn die Verschiebung signifikant ist
                    {
                        var lineStart = comp.Centroid;
                        var lineEnd = new Point3d(explodedPosition); // Konvertiere zu Point3d für die Linie
                        
                        // Erstelle die Verbindungslinie
                        var line = new Line(lineStart, lineEnd);
                        var lineAttr = new ObjectAttributes();
                        lineAttr.ColorSource = ObjectColorSource.ColorFromObject;
                        lineAttr.ObjectColor = System.Drawing.Color.DarkGray;
                        
                        var lineId = _doc.Objects.AddLine(line, lineAttr);
                        _newInstanceIds.Add(lineId); // Zur Löschung merken
                    }
                }
            }
            
            _doc.Views.Redraw();
        }

        // Berechnet den Explosionsvektor basierend auf Modus und Zentrum
        private Vector3d CalculateExplosionVector(ExplosionComponent comp, Point3d explosionCenter, string explosionMode)
        {
            Vector3d direction;
            
            switch (explosionMode)
            {
                case "Zentrum":
                    // Vom Zentrum des Masterblocks zur Komponente
                    direction = comp.Centroid - explosionCenter;
                    break;
                    
                case "Relativ":
                    // Vom Komponenten-Schwerpunkt zur Komponente
                    direction = comp.Centroid - _componentsCentroid;
                    break;
                    
                case "Achsen":
                    // Entlang der Hauptachsen (X, Y, Z) basierend auf der Position relativ zum Explosionszentrum
                    var relativePos = comp.Centroid - explosionCenter;
                    
                    // Finde die dominante Achse
                    double absX = Math.Abs(relativePos.X);
                    double absY = Math.Abs(relativePos.Y);
                    double absZ = Math.Abs(relativePos.Z);
                    
                    if (absX >= absY && absX >= absZ)
                        direction = new Vector3d(Math.Sign(relativePos.X), 0, 0);
                    else if (absY >= absX && absY >= absZ)
                        direction = new Vector3d(0, Math.Sign(relativePos.Y), 0);
                    else
                        direction = new Vector3d(0, 0, Math.Sign(relativePos.Z));
                    break;
                    
                default:
                    // Fallback: Vom Zentrum des Masterblocks zur Komponente
                    direction = comp.Centroid - explosionCenter;
                    break;
            }
            
            // Wenn die Richtung Null ist (Komponente im Zentrum), verwende eine deterministische Richtung
            if (direction.IsZero)
            {
                // Deterministische Zufallsrichtung basierend auf der Instanz-ID
                int hash = comp.Instance.Id.GetHashCode();
                var random = new Random(hash);
                direction = new Vector3d(
                    random.NextDouble() * 2 - 1,
                    random.NextDouble() * 2 - 1,
                    random.NextDouble() * 2 - 1
                );
            }
            
            // Normalisiere den Richtungsvektor
            if (!direction.Unitize())
            {
                // Wenn die Unitize-Operation fehlschlägt (sehr unwahrscheinlich), Standardrichtung
                direction = new Vector3d(0, 0, 1);
            }
            
            return direction;
        }

        // Visualisiert die Zentrumspunkte
        private void VisualizeCentroids(Point3d masterCenter)
            {
                // Visualisiere das Explosionszentrum
            var centerSphere = new Sphere(masterCenter, 5.0);
                var centerAttr = new ObjectAttributes();
                centerAttr.ColorSource = ObjectColorSource.ColorFromObject;
                centerAttr.ObjectColor = System.Drawing.Color.Red;
                var centerId = _doc.Objects.AddSphere(centerSphere, centerAttr);
                _centroidMarkerIds.Add(centerId);
                
                // Visualisiere den Komponenten-Schwerpunkt
                var compCenterSphere = new Sphere(_componentsCentroid, 5.0);
                var compCenterAttr = new ObjectAttributes();
                compCenterAttr.ColorSource = ObjectColorSource.ColorFromObject;
                compCenterAttr.ObjectColor = System.Drawing.Color.Blue;
                var compCenterId = _doc.Objects.AddSphere(compCenterSphere, compCenterAttr);
                _centroidMarkerIds.Add(compCenterId);
                
                // Visualisiere die Schwerpunkte der einzelnen Komponenten
                foreach (var comp in _components)
                {
                    var compSphere = new Sphere(comp.Centroid, 2.0);
                    var compAttr = new ObjectAttributes();
                    compAttr.ColorSource = ObjectColorSource.ColorFromObject;
                    compAttr.ObjectColor = System.Drawing.Color.Green;
                    var id = _doc.Objects.AddSphere(compSphere, compAttr);
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
            
            _doc.Views.Redraw();
        }

        private void ApplyChanges()
        {
            // Bestätige die Änderungen und beende
            RhinoApp.WriteLine("Änderungen übernommen.");
            
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
            
            // Lösche die Schwerpunktmarker, aber behalte die neuen Instanzen
            foreach (var id in _centroidMarkerIds)
            {
                _doc.Objects.Delete(id, true);
            }
            _centroidMarkerIds.Clear();
            
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