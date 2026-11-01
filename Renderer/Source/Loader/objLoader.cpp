/************************************************
* objLoader.cpp
* objモデルを読み込むクラス
*
* 最終更新日:2026/09/24
* 作成者：Keigo Onari
*
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "objLoader.h"
#include <sstream>
#include <string>
#include <vector>
#include <filesystem>
#include <algorithm>

#include "../../../Core/Source/Math/Vector/vector.h"
#include "../mesh.h"
#include "../material.h"

// 仮
#include "../../../Core/Source/Log/logger.h"

namespace Renderer
{
    ObjLoader::ObjLoader() {}
    ObjLoader::~ObjLoader() {}

    // =============================================================
    // 面の1頂点(v/vt/vn)をパースするヘルパー関数
    // =============================================================
    static void ParseFaceVertex(const std::string& vertexStr, int& v, int& vt, int& vn)
    {
        v = vt = vn = 0;
        std::stringstream ss(vertexStr);
        std::string item;

        if (std::getline(ss, item, '/')) {
            if (!item.empty()) v = std::stoi(item);
        }
        if (std::getline(ss, item, '/')) {
            if (!item.empty()) vt = std::stoi(item);
        }
        if (std::getline(ss, item, '/')) {
            if (!item.empty()) vn = std::stoi(item);
        }
    }

    // =============================================================
    // モデルのロード
    // =============================================================
    Mesh* ObjLoader::LoadModel(const std::wstring& fName)
    {
        if (_file.OpenFile(fName.c_str(), Core::_READ_ONRY) == CORE_FAILED)
        {
            const std::wstring log(L"Failed open file ->" + fName);
            Core::Logger::Log(log.c_str());
            return nullptr;
        }

        const std::wstring log(L"Succeeded open file ->" + fName);
        Core::Logger::Log(log.c_str());
        Core::Logger::Log("Getted ifstream");
        std::string str;

        Core::Logger::Log("Sengen Variable");

        const std::string KEY_MTLLIB = "mtllib ";
        const std::string KEY_USEMTL = "usemtl ";
        const std::string KEY_VT = "vt ";
        const std::string KEY_VN = "vn ";
        const std::string KEY_V = "v ";
        const std::string KEY_F = "f ";

        Mesh* mesh = new Mesh();

        std::vector<std::string> newmtlName;
        std::vector<Core::Vector3> position;
        std::vector<Core::Vector3> normal;
        std::vector<Core::Vector2> texcoord;

        std::vector<int> positionIndex;
        std::vector<int> normalIndex;
        std::vector<int> texcoordIndex;
        std::vector<unsigned int> indices;

        unsigned int indicesIndex = 0;

        // 生ポインタでマテリアルを保持
        std::vector<Material*> materials;

        _newmtlName.clear();

        Core::Logger::Log("Start model load");

        while (_file.Getline(str))
        {
            if (str.empty() || str[0] == '#')
                continue;

            if (str.compare(0, KEY_MTLLIB.length(), KEY_MTLLIB) == 0)
            {
                str.erase(0, KEY_MTLLIB.length());

                // 複数のmtllibが呼ばれた場合、以前のマテリアルを破棄してメモリリークを防ぐ
                for (Material* m : materials) {
                    delete m;
                }
                materials.clear();

                materials = loadMaterial(removeFilenameFromPath(wstringToString(fName)) + "/" + str);
            }
            else if (str.compare(0, KEY_USEMTL.length(), KEY_USEMTL) == 0)
            {
                str.erase(0, KEY_USEMTL.length());
                newmtlName.push_back(str);

                if (!indices.empty())
                {
                    mesh->Indecies.push_back(indices);
                    indices.clear();
                }
            }
            else if (str.compare(0, KEY_VT.length(), KEY_VT) == 0)
            {
                str.erase(0, KEY_VT.length());
                std::istringstream iss(str);
                Core::Vector2 tmp;
                iss >> tmp._x >> tmp._y;
                tmp._y = 1.0f - tmp._y;
                texcoord.push_back(tmp);
            }
            else if (str.compare(0, KEY_VN.length(), KEY_VN) == 0)
            {
                str.erase(0, KEY_VN.length());
                std::istringstream iss(str);
                Core::Vector3 tmp;
                iss >> tmp._x >> tmp._y >> tmp._z;
                normal.push_back(tmp);
            }
            else if (str.compare(0, KEY_V.length(), KEY_V) == 0)
            {
                str.erase(0, KEY_V.length());
                std::istringstream iss(str);
                Core::Vector3 tmp;
                iss >> tmp._x >> tmp._y >> tmp._z;
                position.push_back(tmp);
            }
            else if (str.compare(0, KEY_F.length(), KEY_F) == 0)
            {
                str.erase(0, KEY_F.length());
                std::istringstream iss(str);
                std::string vertexStr;

                struct FaceVert { int v, vt, vn; };
                std::vector<FaceVert> faceVerts;

                while (iss >> vertexStr)
                {
                    FaceVert fv;
                    ParseFaceVertex(vertexStr, fv.v, fv.vt, fv.vn);
                    faceVerts.push_back(fv);
                }

                if (faceVerts.size() >= 3)
                {
                    for (size_t i = 1; i < faceVerts.size() - 1; ++i)
                    {
                        const FaceVert* tri[3] = { &faceVerts[0], &faceVerts[i], &faceVerts[i + 1] };
                        for (int k = 0; k < 3; ++k)
                        {
                            positionIndex.push_back(tri[k]->v);
                            texcoordIndex.push_back(tri[k]->vt);
                            normalIndex.push_back(tri[k]->vn);
                            indices.push_back(indicesIndex++);
                        }
                    }
                }
            }
        }

        if (!indices.empty())
        {
            mesh->Indecies.push_back(indices);
        }

        mesh->Vertices.resize(positionIndex.size());
        for (size_t i = 0; i < positionIndex.size(); i++)
        {
            if (positionIndex[i] > 0) mesh->Vertices[i].Position = position[positionIndex[i] - 1];
            if (texcoordIndex[i] > 0) mesh->Vertices[i].UV = texcoord[texcoordIndex[i] - 1];
            if (normalIndex[i] > 0)   mesh->Vertices[i].Normal = normal[normalIndex[i] - 1];
        }

        _file.CloseFile();

        // マテリアルの順序の整理と所有権の移動
        mesh->Materials.resize(newmtlName.size());
        int matIndex = 0;
        for (const auto& name : newmtlName)
        {
            auto it = std::find(_newmtlName.begin(), _newmtlName.end(), name);
            if (it != _newmtlName.end())
            {
                size_t index = std::distance(_newmtlName.begin(), it);
                if (index < materials.size() && materials[index] != nullptr)
                {
                    mesh->Materials[matIndex] = materials[index];
                    // mesh側に所有権を渡したため、解放されないようnullptrにする
                    materials[index] = nullptr;
                }
            }
            matIndex++;
        }

        // meshに渡されず、使われなかった余剰マテリアルを破棄する
        for (Material* mat : materials)
        {
            if (mat != nullptr)
            {
                delete mat;
            }
        }
        materials.clear();

        return mesh;
    }

    // =====================================================================================
    std::string ObjLoader::wstringToString(const std::wstring& wstr)
    {
        if (wstr.empty()) return std::string();
        int sizeNeeded = WideCharToMultiByte(CP_UTF8, 0, &wstr[0], (int)wstr.size(), NULL, 0, NULL, NULL);
        std::string strTo(sizeNeeded, 0);
        WideCharToMultiByte(CP_UTF8, 0, &wstr[0], (int)wstr.size(), &strTo[0], sizeNeeded, NULL, NULL);
        return strTo;
    }

    std::wstring ObjLoader::stringToWstring(const std::string& str)
    {
        if (str.empty()) return std::wstring();
        int sizeNeeded = MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), NULL, 0);
        std::wstring wstrTo(sizeNeeded, 0);
        MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), &wstrTo[0], sizeNeeded);
        return wstrTo;
    }

    // =============================================================
    // マテリアルのロード
    // =============================================================
    std::vector<Material*> ObjLoader::loadMaterial(const std::string& fName)
    {
        if (!_mtlFile.OpenFile(fName.c_str(), Core::_READ_ONRY))
            return std::vector<Material*>();

        std::ifstream& ifs = _mtlFile.GetIfstream();
        std::string str;

        const std::string KEY_NEWMTL = "newmtl ";
        const std::string KEY_MAP_KD = "map_Kd ";
        const std::string KEY_KA = "Ka ";
        const std::string KEY_KD = "Kd ";
        const std::string KEY_KS = "Ks ";

        std::vector<Material*> materials;
        Material* material = nullptr; // 生ポインタに変更

        while (std::getline(ifs, str))
        {
            if (str.empty() || str[0] == '#')
                continue;

            if (str.compare(0, KEY_NEWMTL.length(), KEY_NEWMTL) == 0)
            {
                str.erase(0, KEY_NEWMTL.length());
                _newmtlName.push_back(str);

                if (material != nullptr)
                {
                    materials.push_back(material);
                }
                // newでメモリを確保
                material = new Material();
            }
            else if (material != nullptr)
            {
                if (str.compare(0, KEY_KA.length(), KEY_KA) == 0)
                {
                    str.erase(0, KEY_KA.length());
                    std::istringstream iss(str);
                    iss >> material->Ambient._x >> material->Ambient._y >> material->Ambient._z;
                    material->Ambient._w = 1.0f;
                }
                else if (str.compare(0, KEY_KD.length(), KEY_KD) == 0)
                {
                    str.erase(0, KEY_KD.length());
                    std::istringstream iss(str);
                    iss >> material->Diffuse._x >> material->Diffuse._y >> material->Diffuse._z;
                    material->Diffuse._w = 1.0f;
                }
                else if (str.compare(0, KEY_KS.length(), KEY_KS) == 0)
                {
                    str.erase(0, KEY_KS.length());
                    std::istringstream iss(str);
                    iss >> material->Specular._x >> material->Specular._y >> material->Specular._z;
                    material->Specular._w = 1.0f;
                }
                else if (str.compare(0, KEY_MAP_KD.length(), KEY_MAP_KD) == 0)
                {
                    str.erase(0, KEY_MAP_KD.length());
                    std::string texPath = removeFilenameFromPath(fName) + "/" + str;
                    material->TexturePath = stringToWstring(texPath);
                    material->UseTexture = true;
                }
            }
        }

        if (material != nullptr)
        {
            materials.push_back(material);
        }

        _mtlFile.CloseFile();
        return materials;
    }

    // =============================================================
    // ユーティリティ系
    // =============================================================
    bool ObjLoader::containsSlashesCount(const std::wstring& line, const int count) {
        return std::count(line.begin(), line.end(), '/') == count;
    }
    bool ObjLoader::containsSlashesCount(const std::string& line, const int count) {
        return std::count(line.begin(), line.end(), '/') == count;
    }
    bool ObjLoader::containsSlashesOverCount(const std::wstring& line, const int count) {
        return std::count(line.begin(), line.end(), '/') >= count;
    }
    bool ObjLoader::containsSlashesOverCount(const std::string& line, const int count) {
        return std::count(line.begin(), line.end(), '/') >= count;
    }
    int ObjLoader::containsSlashesCount(const std::wstring& line) {
        return static_cast<int>(std::count(line.begin(), line.end(), '/'));
    }
    int ObjLoader::containsSlashesCount(const std::string& line) {
        return static_cast<int>(std::count(line.begin(), line.end(), '/'));
    }

    std::string ObjLoader::removeFilenameFromPath(const std::string& filePath)
    {
        std::filesystem::path path(filePath);
        return path.parent_path().string();
    }
}