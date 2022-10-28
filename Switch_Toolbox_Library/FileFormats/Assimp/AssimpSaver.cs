using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Assimp;
using OpenTK;
using Toolbox.Library.Rendering;
using System.Windows.Forms;
using Toolbox.Library.Animations;
using Toolbox.Library.Forms;

namespace Toolbox.Library
{
    public class AssimpSaver
    {
        private List<string> _extractedTextures = new List<string>();
        public List<string> BoneNames = new List<string>();
        STProgressBar _progressBar;

        public void SaveFromModel(STGenericModel model, string fileName, List<STGenericTexture> textures, STSkeleton skeleton = null, List<int> nodeArray = null)
        {
            SaveFromModel(model.Objects.ToList(), model.Materials.ToList(), fileName, textures, skeleton, nodeArray);
        }

        public void SaveFromModel(List<STGenericObject> meshes, List<STGenericMaterial> materials, string fileName, List<STGenericTexture> textures, STSkeleton skeleton = null, List<int> nodeArray = null)
        {
            _extractedTextures.Clear();

            var scene = new Scene
            {
                RootNode = new Node("RootNode")
            };

            _progressBar = new STProgressBar();
            _progressBar.Task = "Exporting Skeleton...";
            _progressBar.Value = 0;
            _progressBar.StartPosition = FormStartPosition.CenterScreen;
            _progressBar.Show();
            _progressBar.Refresh();

            SaveSkeleton(skeleton, scene.RootNode);
            SaveMaterials(scene, materials, fileName, textures);

            _progressBar.Task = "Exporting Meshes...";
            _progressBar.Value = 50;

            SaveMeshes(scene, meshes, skeleton, fileName, nodeArray);

            _progressBar.Task = "Saving File...";
            _progressBar.Value = 80;

            SaveScene(fileName, scene, meshes);

            _progressBar.Value = 100;
            _progressBar.Close();
            _progressBar.Dispose();
        }

        private void SaveScene(string fileName, Scene scene, List<STGenericObject> meshes)
        {
            using (var v = new AssimpContext())
            {
                var ext = System.IO.Path.GetExtension(fileName);

                var formatId = "collada";
                if (ext == ".obj")
                    formatId = "obj";
                if (ext == ".3ds")
                    formatId = "3ds";
                if (ext == ".dae")
                    formatId = "collada";
                if (ext == ".ply")
                    formatId = "ply";

                var exportSuccessScene = v.ExportFile(scene, fileName, formatId, PostProcessSteps.FlipUVs);
                if (exportSuccessScene)
                {
                    if (ext == ".dae")
                        WriteExtraSkinningInfo(fileName, scene, meshes);

                    MessageBox.Show($"Exported {fileName} Successfuly!");
                }
                else
                    MessageBox.Show($"Failed to export {fileName}!");
            }

        }

        private void SaveMeshes(Scene scene, List<STGenericObject> meshes, STSkeleton skeleton, string fileName, List<int> nodeArray)
        {
            var meshIndex = 0;
            foreach (var mesh in meshes.Select(obj => SaveMesh(obj, scene, meshIndex++, skeleton, nodeArray)))
            {
                scene.Meshes.Add(mesh);
            }
            var geomNode = new Node(Path.GetFileNameWithoutExtension(fileName), scene.RootNode);

            for (var ob = 0; ob < scene.MeshCount; ob++)
            {
                geomNode.MeshIndices.Add(ob);
            }

            scene.RootNode.Children.Add(geomNode);
        }

        private Mesh SaveMesh(STGenericObject genericObj, Scene scene, int index, STSkeleton skeleton, List<int> nodeArray)
        {
            //Assimp is weird so use mesh_# for the name. We'll change it back after save
            var mesh = new Mesh($"mesh_{ index }", PrimitiveType.Triangle);

            if (genericObj.MaterialIndex < scene.MaterialCount && genericObj.MaterialIndex > 0)
                mesh.MaterialIndex = genericObj.MaterialIndex;
            else
                mesh.MaterialIndex = 0;

            var textureCoords0 = new List<Vector3D>();
            var textureCoords1 = new List<Vector3D>();
            var textureCoords2 = new List<Vector3D>();
            var vertexColors = new List<Color4D>();

            var vertexId = 0;
            foreach (var v in genericObj.vertices)
            {
                mesh.Vertices.Add(new Vector3D(v.pos.X, v.pos.Y, v.pos.Z));
                mesh.Normals.Add(new Vector3D(v.nrm.X, v.nrm.Y, v.nrm.Z));
                textureCoords0.Add(new Vector3D(v.uv0.X, v.uv0.Y, 0));
                textureCoords1.Add(new Vector3D(v.uv1.X, v.uv1.Y, 0));
                textureCoords2.Add(new Vector3D(v.uv2.X, v.uv2.Y, 0));
                vertexColors.Add(new Color4D(v.col.X, v.col.Y, v.col.Z, v.col.W));

                if (skeleton != null)
                {
                    for (var j = 0; j < v.boneIds.Count; j++)
                    {
                        if (j < genericObj.VertexSkinCount)
                        {
                            STBone sTbone = nodeArray != null ? skeleton.bones[nodeArray[v.boneIds[j]]] : skeleton.bones[v.boneIds[j]];

                            //Find the index of a bone. If it doesn't exist then we add it
                            var boneInd = mesh.Bones.FindIndex(x => x.Name == sTbone.Text);

                            if (boneInd == -1)
                            {
                                var matrices = Toolbox.Library.IO.MatrixExenstion.CalculateInverseMatrix(sTbone);

                                //Set the inverse matrix
                                var transform = matrices.inverse.FromNumerics();

                                //Create a new assimp bone
                                var bone = new Bone
                                {
                                    Name = sTbone.Text,
                                    OffsetMatrix = sTbone.invert.ToMatrix4x4()
                                };
                                mesh.Bones.Add(bone);
                                BoneNames.Add(bone.Name);

                                boneInd = mesh.Bones.IndexOf(bone); //Set the index of the bone for the vertex weight
                            }

                            var minWeightAmount = 0;

                            //Check if the max amount of weights is higher than the current bone id
                            if (v.boneWeights.Count > j && v.boneWeights[j] > minWeightAmount)
                            {
                                if (v.boneWeights[j] <= 1)
                                    mesh.Bones[boneInd].VertexWeights.Add(new VertexWeight(vertexId, v.boneWeights[j]));
                                else
                                    mesh.Bones[boneInd].VertexWeights.Add(new VertexWeight(vertexId, 1));
                            }
                            else if (v.boneWeights.Count == 0 || v.boneWeights[j] > minWeightAmount)
                                mesh.Bones[boneInd].VertexWeights.Add(new VertexWeight(vertexId, 1));
                        }
                    }
                }


                vertexId++;
            }

            if (genericObj.lodMeshes.Count != 0)
            {
                var faces = genericObj.lodMeshes[genericObj.DisplayLODIndex].faces;
                for (var f = 0; f < faces.Count; f++)
                    mesh.Faces.Add(new Face(new int[] { faces[f++], faces[f++], faces[f] }));
            }
            if (genericObj.PolygonGroups.Count != 0)
            {
                for (var p = 0; p < genericObj.PolygonGroups.Count; p++)
                {
                    var polygonGroup = genericObj.PolygonGroups[p];
                    for (var f = 0; f < polygonGroup.faces.Count; f++)
                        if (f < polygonGroup.faces.Count - 2)
                            mesh.Faces.Add(new Face(new int[] { polygonGroup.faces[f++], polygonGroup.faces[f++], polygonGroup.faces[f] }));
                }
            }

            mesh.TextureCoordinateChannels.SetValue(textureCoords0, 0);
            mesh.TextureCoordinateChannels.SetValue(textureCoords1, 1);
            mesh.TextureCoordinateChannels.SetValue(textureCoords2, 2);
            mesh.VertexColorChannels.SetValue(vertexColors, 0);

            return mesh;
        }

        //Extra skin data based on https://github.com/Sage-of-Mirrors/SuperBMD/blob/ce1061e9b5f57de112f1d12f6459b938594664a0/SuperBMDLib/source/Model.cs#L193
        //Todo this doesn't quite work yet
        //Need to adjust all mesh name IDs so they are correct
        private void WriteExtraSkinningInfo(string fileName, Scene outScene, List<STGenericObject> meshes)
        {
            var test = new StreamWriter(fileName + ".tmp");
            var dae = File.OpenText(fileName);

            var geomIndex = 0;
            while (!dae.EndOfStream)
            {
                var line = dae.ReadLine();

                /* if (line == "  <library_visual_scenes>")
                 {
                     AddControllerLibrary(outScene, test);
                     test.WriteLine(line);
                     test.Flush();
                 }
                 else if (line.Contains("<node"))
                 {
                  //   test.WriteLine(line);
                    // test.Flush();

                     string[] testLn = line.Split('\"');
                     string name = testLn[3];

                     string jointLine = line.Replace(">", $" sid=\"{ name }\" type=\"JOINT\">");
                     test.WriteLine(jointLine);
                     test.Flush();
                 }
                 else if (line.Contains("</visual_scene>"))
                 {
                     foreach (Mesh mesh in outScene.Meshes)
                     {
                         test.WriteLine($"      <node id=\"{ mesh.Name }\" name=\"{ mesh.Name }\" type=\"NODE\">");

                         test.WriteLine($"       <instance_controller url=\"#{ mesh.Name }-skin\">");
                         test.WriteLine("        <skeleton>#skeleton_root</skeleton>");
                         test.WriteLine("        <bind_material>");
                         test.WriteLine("         <technique_common>");
                         test.WriteLine($"          <instance_material symbol=\"theresonlyone\" target=\"#m{ mesh.MaterialIndex }mat\" />");
                         test.WriteLine("         </technique_common>");
                         test.WriteLine("        </bind_material>");
                         test.WriteLine("       </instance_controller>");

                         test.WriteLine("      </node>");
                         test.Flush();
                     }

                     test.WriteLine(line);
                     test.Flush();
                 }*/
                if (line.Contains("<geometry"))
                {
                    var realMeshName = meshes[geomIndex].Text;
                    test.WriteLine($"    <geometry id=\"meshId{ geomIndex }\" name=\"{ realMeshName }\" > ");
                    test.Flush();

                    geomIndex++;
                }
                else
                {
                    test.WriteLine(line);
                    test.Flush();
                }

                /*    else if (line.Contains("<matrix"))
                    {
                        string matLine = line.Replace("<matrix>", "<matrix sid=\"matrix\">");
                        test.WriteLine(matLine);
                        test.Flush();
                    }*/

            }

            test.Close();
            dae.Close();

            File.Copy(fileName + ".tmp", fileName, true);
            File.Delete(fileName + ".tmp");
        }

        private void AddControllerLibrary(Scene scene, StreamWriter writer)
        {
            writer.WriteLine("  <library_controllers>");

            for (var i = 0; i < scene.MeshCount; i++)
            {
                var curMesh = scene.Meshes[i];
                curMesh.Name = curMesh.Name.Replace('_', '-');

                writer.WriteLine($"   <controller id=\"{ curMesh.Name }-skin\" name=\"{ curMesh.Name }Skin\">");

                writer.WriteLine($"    <skin source=\"#meshId{ i }\">");

                WriteBindShapeMatrixToStream(writer);
                WriteJointNameArrayToStream(curMesh, writer);
                WriteInverseBindMatricesToStream(curMesh, writer);
                WriteSkinWeightsToStream(curMesh, writer);

                writer.WriteLine("     <joints>");

                writer.WriteLine($"      <input semantic=\"JOINT\" source=\"#{ curMesh.Name }-skin-joints-array\"></input>");
                writer.WriteLine($"      <input semantic=\"INV_BIND_MATRIX\" source=\"#{ curMesh.Name }-skin-bind_poses-array\"></input>");

                writer.WriteLine("     </joints>");
                writer.Flush();

                WriteVertexWeightsToStream(curMesh, writer);

                writer.WriteLine("    </skin>");

                writer.WriteLine("   </controller>");
                writer.Flush();
            }

            writer.WriteLine("  </library_controllers>");
            writer.Flush();
        }

        private void WriteJointNameArrayToStream(Mesh mesh, StreamWriter writer)
        {
            writer.WriteLine($"      <source id =\"{ mesh.Name }-skin-joints-array\">");
            writer.WriteLine($"      <Name_array id=\"{ mesh.Name }-skin-joints-array\" count=\"{ mesh.Bones.Count }\">");

            writer.Write("       ");
            foreach (var bone in mesh.Bones)
            {
                writer.Write($"{ bone.Name }");
                if (bone != mesh.Bones.Last())
                    writer.Write(' ');
                else
                    writer.Write('\n');

                writer.Flush();
            }

            writer.WriteLine("      </Name_array>");
            writer.Flush();

            writer.WriteLine("      <technique_common>");
            writer.WriteLine($"       <accessor source=\"#{ mesh.Name }-skin-joints-array\" count=\"{ mesh.Bones.Count }\" stride=\"1\">");
            writer.WriteLine("         <param name=\"JOINT\" type=\"Name\"></param>");
            writer.WriteLine("       </accessor>");
            writer.WriteLine("      </technique_common>");
            writer.WriteLine("      </source>");
            writer.Flush();
        }

        private void WriteInverseBindMatricesToStream(Mesh mesh, StreamWriter writer)
        {
            writer.WriteLine($"      <source id =\"{ mesh.Name }-skin-bind_poses-array\">");
            writer.WriteLine($"      <float_array id=\"{ mesh.Name }-skin-bind_poses-array\" count=\"{ mesh.Bones.Count * 16 }\">");

            foreach (var bone in mesh.Bones)
            {
                var ibm = bone.OffsetMatrix;
                ibm.Transpose();

                writer.WriteLine($"       {ibm.A1.ToString("F")} {ibm.A2.ToString("F")} {ibm.A3.ToString("F")} {ibm.A4.ToString("F")}");
                writer.WriteLine($"       {ibm.B1.ToString("F")} {ibm.B2.ToString("F")} {ibm.B3.ToString("F")} {ibm.B4.ToString("F")}");
                writer.WriteLine($"       {ibm.C1.ToString("F")} {ibm.C2.ToString("F")} {ibm.C3.ToString("F")} {ibm.C4.ToString("F")}");
                writer.WriteLine($"       {ibm.D1.ToString("F")} {ibm.D2.ToString("F")} {ibm.D3.ToString("F")} {ibm.D4.ToString("F")}");

                if (bone != mesh.Bones.Last())
                    writer.WriteLine("");
            }

            writer.WriteLine("      </float_array>");
            writer.Flush();

            writer.WriteLine("      <technique_common>");
            writer.WriteLine($"       <accessor source=\"#{ mesh.Name }-skin-bind_poses-array\" count=\"{ mesh.Bones.Count }\" stride=\"16\">");
            writer.WriteLine("         <param name=\"TRANSFORM\" type=\"float4x4\"></param>");
            writer.WriteLine("       </accessor>");
            writer.WriteLine("      </technique_common>");
            writer.WriteLine("      </source>");
            writer.Flush();
        }

        private void WriteSkinWeightsToStream(Mesh mesh, StreamWriter writer)
        {
            var totalWeightCount = 0;

            foreach (var bone in mesh.Bones)
            {
                totalWeightCount += bone.VertexWeightCount;
            }

            writer.WriteLine($"      <source id =\"{ mesh.Name }-skin-weights-array\">");
            writer.WriteLine($"      <float_array id=\"{ mesh.Name }-skin-weights-array\" count=\"{ totalWeightCount }\">");
            writer.Write("       ");

            foreach (var bone in mesh.Bones)
            {
                foreach (var weight in bone.VertexWeights)
                {
                    writer.Write($"{ weight.Weight } ");
                }

                if (bone == mesh.Bones.Last())
                    writer.WriteLine();
            }

            writer.WriteLine("      </float_array>");
            writer.Flush();

            writer.WriteLine("      <technique_common>");
            writer.WriteLine($"       <accessor source=\"#{ mesh.Name }-skin-weights-array\" count=\"{ totalWeightCount }\" stride=\"1\">");
            writer.WriteLine("         <param name=\"WEIGHT\" type=\"float\"></param>");
            writer.WriteLine("       </accessor>");
            writer.WriteLine("      </technique_common>");
            writer.WriteLine("      </source>");
            writer.Flush();
        }

        private class RiggedWeight
        {
            public List<float> Weights { get; private set; }
            public List<int> BoneIndices { get; private set; }

            public int WeightCount { get; private set; }

            public RiggedWeight()
            {
                Weights = new List<float>();
                BoneIndices = new List<int>();
            }

            public void AddWeight(float weight, int boneIndex)
            {
                Weights.Add(weight);
                BoneIndices.Add(boneIndex);
                WeightCount++;
            }
        }

        private void WriteVertexWeightsToStream(Mesh mesh, StreamWriter writer)
        {
            var weights = new List<float>();
            var vertIdWeights = new Dictionary<int, RiggedWeight>();

            foreach (var bone in mesh.Bones)
            {
                foreach (var weight in bone.VertexWeights)
                {
                    weights.Add(weight.Weight);

                    if (!vertIdWeights.ContainsKey(weight.VertexID))
                        vertIdWeights.Add(weight.VertexID, new RiggedWeight());

                    vertIdWeights[weight.VertexID].AddWeight(weight.Weight, mesh.Bones.IndexOf(bone));
                }
            }

            writer.WriteLine($"      <vertex_weights count=\"{ vertIdWeights.Count }\">");

            writer.WriteLine($"       <input semantic=\"JOINT\" source=\"#{ mesh.Name }-skin-joints-array\" offset=\"0\"></input>");
            writer.WriteLine($"       <input semantic=\"WEIGHT\" source=\"#{ mesh.Name }-skin-weights-array\" offset=\"1\"></input>");

            writer.WriteLine("       <vcount>");

            writer.Write("        ");
            for (var i = 0; i < vertIdWeights.Count; i++)
                writer.Write($"{ vertIdWeights[i].WeightCount } ");

            writer.WriteLine("\n       </vcount>");

            writer.WriteLine("       <v>");
            writer.Write("        ");

            for (var i = 0; i < vertIdWeights.Count; i++)
            {
                var curWeight = vertIdWeights[i];

                for (var j = 0; j < curWeight.WeightCount; j++)
                {
                    writer.Write($"{ curWeight.BoneIndices[j] } { weights.IndexOf(curWeight.Weights[j]) } ");
                }
            }

            writer.WriteLine("\n       </v>");

            writer.WriteLine($"      </vertex_weights>");
        }

        private void WriteBindShapeMatrixToStream(StreamWriter writer)
        {
            writer.WriteLine("     <bind_shape_matrix>");

            writer.WriteLine("      1 0 0 0");
            writer.WriteLine("      0 1 0 0");
            writer.WriteLine("      0 0 1 0");
            writer.WriteLine("      0 0 0 1");

            writer.WriteLine("     </bind_shape_matrix>");
            writer.Flush();
        }

        private void SaveMaterials(Scene scene, List<STGenericMaterial> materials, string fileName, List<STGenericTexture> textures)
        {
            var textureExtension = ".png";
            var texturePath = System.IO.Path.GetDirectoryName(fileName);

            for (var i = 0; i < textures.Count; i++)
            {
                var path = System.IO.Path.Combine(texturePath, textures[i].Text + textureExtension);

                if (!_extractedTextures.Contains(path))
                {
                    _extractedTextures.Add(path);

                    _progressBar.Task = $"Exporting Texture {textures[i].Text}";
                    _progressBar.Value = ((i * 100) / textures.Count);
                    _progressBar.Refresh();

                    var bitmap = textures[i].GetBitmap();
                    bitmap.Save(path);
                    bitmap.Dispose();

                    GC.Collect();
                }
            }

            if (materials.Count == 0)
            {
                var material = new Material();
                material.Name = "New Material";
                scene.Materials.Add(material);
                return;
            }

            foreach (var mat in materials)
            {
                var genericMat = (STGenericMaterial)mat;

                var material = new Material();
                material.Name = genericMat.Text;

                foreach (var tex in genericMat.TextureMaps)
                {
                    var index = textures.FindIndex(r => r.Text.Equals(tex.Name));

                    var path = System.IO.Path.Combine(texturePath, tex.Name + textureExtension);

                    if (!File.Exists(path))
                        continue;

                    var slot2 = new TextureSlot(path, ConvertToAssimpTextureType(tex.Type), 0, TextureMapping.FromUV,
                            0, 1.0f, Assimp.TextureOperation.Add, ConvertToAssimpWrapType(tex.WrapModeS), ConvertToAssimpWrapType(tex.WrapModeT), 0);

                    material.AddMaterialTexture(ref slot2);
                }
                scene.Materials.Add(material);
            }

        }

        private static Assimp.TextureWrapMode ConvertToAssimpWrapType(STTextureWrapMode type)
        {
            switch (type)
            {
                case STTextureWrapMode.Repeat: return TextureWrapMode.Wrap;
                case STTextureWrapMode.Mirror: return TextureWrapMode.Mirror;
                case STTextureWrapMode.Clamp: return TextureWrapMode.Clamp;
                default:
                    return TextureWrapMode.Wrap;
            }
        }

        private static Assimp.TextureType ConvertToAssimpTextureType(STGenericMatTexture.TextureType type)
        {
            switch (type)
            {
                case STGenericMatTexture.TextureType.Diffuse: return TextureType.Diffuse;
                case STGenericMatTexture.TextureType.AO: return TextureType.Ambient;
                case STGenericMatTexture.TextureType.Normal: return TextureType.Normals;
                case STGenericMatTexture.TextureType.Light: return TextureType.Lightmap;
                case STGenericMatTexture.TextureType.Emission: return TextureType.Emissive;
                case STGenericMatTexture.TextureType.Specular: return TextureType.Specular;
                default:
                    return TextureType.Unknown;
            }
        }

        public void SaveFromObject(STGenericObject genericObject, string fileName)
        {
            var scene = new Scene();
            scene.RootNode = new Node("Root");

            var mesh = SaveMesh(genericObject, scene, 0, null, null);
            mesh.MaterialIndex = 0;
            scene.Meshes.Add(mesh);

            var material = new Material();
            material.Name = "NewMaterial";
            scene.Materials.Add(material);

            SaveScene(fileName, scene, new List<STGenericObject>() { genericObject });
        }

        private void SaveSkeleton(STSkeleton skeleton, Node parentNode)
        {
            var root = new Node("skeleton_root");
            parentNode.Children.Add(root);

            Console.WriteLine($"bones {skeleton.bones.Count}");

            if (skeleton.bones.Count > 0)
            {
                foreach (var bone in skeleton.bones)
                {
                    //Get each root bone and find children
                    if (bone.parentIndex == -1)
                    {
                        var boneNode = new Node(bone.Text);
                        boneNode.Transform = AssimpHelper.GetBoneMatrix(bone);
                        root.Children.Add(boneNode);

                        foreach (var child in bone.GetChildren())
                            SaveBones(boneNode, child, skeleton);
                    }
                }
            }
        }
        private void SaveBones(Node parentBone, STBone bone, STSkeleton skeleton)
        {
            var boneNode = new Node(bone.Text);
            parentBone.Children.Add(boneNode);

            boneNode.Transform = AssimpHelper.GetBoneMatrix(bone);

            foreach (var child in bone.GetChildren())
                SaveBones(boneNode, child, skeleton);
        }
    }
}
