# BLOC 1 — Connexion & Lobby : Revue technique détaillée

> **Date :** 2026-04-03
> **Branche :** `develop`
> **Build :** ✅ 0 erreur · 0 avertissement
> **User Stories couvertes :** US-01, US-02, US-03, US-04, US-05, US-06

---

## Table des matières

1. [Vue d'ensemble des changements](#1-vue-densemble-des-changements)
2. [Modifications des fichiers de projet (.csproj)](#2-modifications-des-fichiers-de-projet-csproj)
3. [Domain — Entités métier](#3-domain--entités-métier)
4. [Shared — Paquets réseau](#4-shared--paquets-réseau)
5. [GameLogic — Interfaces applicatives](#5-gamelogic--interfaces-applicatives)
6. [Infrastructure — Implémentation réseau](#6-infrastructure--implémentation-réseau)
7. [Client — Services](#7-client--services)
8. [Client — Convertisseurs XAML](#8-client--convertisseurs-xaml)
9. [Client — ViewModels](#9-client--viewmodels)
10. [Client — Vues AXAML](#10-client--vues-axaml)
11. [Fichiers existants modifiés](#11-fichiers-existants-modifiés)
12. [Architecture & flux de données](#12-architecture--flux-de-données)
13. [Décisions techniques & compromis](#13-décisions-techniques--compromis)
14. [Points d'attention pour la suite](#14-points-dattention-pour-la-suite)

---

## 1. Vue d'ensemble des changements

Le BLOC 1 pose l'intégralité de l'infrastructure de connexion et de lobby du jeu.
Avant ce bloc, la solution contenait uniquement des squelettes vides (`Class1.cs`) et une
fenêtre Avalonia affichant "Welcome to Avalonia!".

Après le BLOC 1, l'application est capable de :

- Afficher un écran de connexion avec saisie du pseudo, de l'adresse serveur et choix de couleur
- Tenter une connexion TCP réelle vers un serveur via LiteNetLib
- Afficher un indicateur coloré de l'état de connexion en temps réel
- Naviguer automatiquement vers le lobby une fois la connexion confirmée par le serveur
- Afficher la liste des joueurs connectés dans le lobby avec leur vaisseau coloré
- Afficher un compte à rebours animé avant le début de la partie

**Statistiques :**

| Catégorie | Nombre |
|-----------|--------|
| Fichiers créés | 19 |
| Fichiers modifiés | 5 |
| Fichiers supprimés (placeholders) | 4 |
| Lignes de code ajoutées (estimation) | ~950 |
| Erreurs de build corrigées | 2 |

---

## 2. Modifications des fichiers de projet (.csproj)

### 2.1 `AsteroidOnline.Shared.csproj`

**Avant :** aucune dépendance.

**Après :**
```xml
<ProjectReference Include="..\AsteroidOnline.Domain\AsteroidOnline.Domain.csproj" />
```

**Pourquoi :** Les paquets réseau (ex. `ConnectRequestPacket`, `LobbyStatePacket`) référencent
l'enum `PlayerColor` qui réside dans Domain. Conformément aux règles Clean Architecture du projet,
Shared dépend de Domain.

---

### 2.2 `AsteroidOnline.Client.csproj`

**Avant :** référençait uniquement `AsteroidOnline.Shared` + packages Avalonia + CommunityToolkit.Mvvm + LiteNetLib.

**Après :**
```xml
<ProjectReference Include="..\AsteroidOnline.Infrastructure\AsteroidOnline.Infrastructure.csproj" />
<ProjectReference Include="..\AsteroidOnline.Shared\AsteroidOnline.Shared.csproj" />
```

Le package **LiteNetLib** a également été retiré du `.csproj` Client : il n'est plus nécessaire
directement dans le Client car `LiteNetClientService` vit dans Infrastructure. Le Client ne
manipule LiteNetLib que transitivement, via l'interface `INetworkClientService`.

**Pourquoi la référence Infrastructure :** Le fichier `App.axaml.cs` joue le rôle de
*composition root* (point d'entrée du conteneur DI). C'est le seul endroit où l'on instancie
`LiteNetClientService` concrètement. Toutes les autres couches (ViewModels) ne voient que
l'interface `INetworkClientService`.

---

## 3. Domain — Entités métier

### `src/AsteroidOnline.Domain/Entities/PlayerColor.cs`

**Répond à :** US-05

```
Domain/
└── Entities/
    └── PlayerColor.cs   ← NOUVEAU
```

Enum `byte` à 6 valeurs :

| Valeur | Nom | Code hex |
|--------|-----|----------|
| 0 | Rouge | `#FF4444` |
| 1 | Bleu | `#4488FF` |
| 2 | Vert | `#44FF88` |
| 3 | Jaune | `#FFDD44` |
| 4 | Violet | `#AA44FF` |
| 5 | Orange | `#FF8844` |

Le type de base `byte` est intentionnel : lors de la sérialisation binaire des paquets,
un seul octet suffit à transmettre la couleur sur le réseau, sans surcoût.

> **Supprimé :** `Domain/Class1.cs` (placeholder vide)

---

## 4. Shared — Paquets réseau

```
Shared/
└── Packets/
    ├── IPacket.cs                ← NOUVEAU
    ├── PacketType.cs             ← NOUVEAU
    ├── ConnectRequestPacket.cs   ← NOUVEAU
    ├── LobbyJoinedPacket.cs      ← NOUVEAU
    ├── LobbyStatePacket.cs       ← NOUVEAU
    └── CountdownPacket.cs        ← NOUVEAU
```

> **Supprimé :** `Shared/Class1.cs` (placeholder vide)

### 4.1 `IPacket.cs` — Interface commune

Tous les paquets implémentent cette interface :

```csharp
public interface IPacket
{
    PacketType Type { get; }
    void Serialize(BinaryWriter writer);
    void Deserialize(BinaryReader reader);
}
```

**Protocole de sérialisation :**
```
[ 1 octet : PacketType ] [ N octets : corps sérialisé via BinaryWriter ]
```

Ce protocole binaire minimaliste a été choisi pour sa performance (pas de JSON, pas d'overhead
XML) et sa compatibilité avec `NetDataWriter`/`NetDataReader` de LiteNetLib.

---

### 4.2 `PacketType.cs` — Identifiants de paquets

Enum `byte` découpé en deux plages :

| Plage | Direction | Paquets |
|-------|-----------|---------|
| `0x01–0x0F` | Client → Serveur | ConnectRequest, PlayerInput, FireInput, DashInput, ColorSelect |
| `0x10–0x1F` | Serveur → Client | LobbyJoined, LobbyState, Countdown, GameStateSnapshot, PlayerEliminated, GameOver, PlayerKilled |

Le BLOC 1 utilise uniquement : `ConnectRequest`, `LobbyJoined`, `LobbyState`, `Countdown`.
Les autres valeurs sont réservées pour les blocs suivants.

---

### 4.3 `ConnectRequestPacket.cs` — US-01

Envoyé par le client juste après l'établissement de la connexion LiteNetLib.

| Champ | Type | Description |
|-------|------|-------------|
| `Pseudo` | `string` | Pseudo du joueur (1–20 caractères) |
| `Color` | `PlayerColor` | Couleur choisie dans le sélecteur |

Sérialisé en `ReliableOrdered` (garantie de livraison et d'ordre).

---

### 4.4 `LobbyJoinedPacket.cs` — US-02

Réponse du serveur confirmant l'entrée dans le lobby.

| Champ | Type | Description |
|-------|------|-------------|
| `PlayerId` | `int` | Identifiant unique attribué par le serveur |
| `Message` | `string` | Message de bienvenue optionnel |

La réception de ce paquet dans `ConnectViewModel` déclenche la navigation vers `LobbyViewModel`.

---

### 4.5 `LobbyStatePacket.cs` — US-04, US-05

Diffusé à tous les clients à chaque changement dans le lobby (arrivée, départ, changement de couleur).

Contient une `List<LobbyPlayerInfo>` où chaque entrée expose :

| Champ | Type | Description |
|-------|------|-------------|
| `Id` | `int` | Identifiant du joueur |
| `Pseudo` | `string` | Pseudo |
| `Color` | `PlayerColor` | Couleur de vaisseau |
| `ColorHex` | `string` (calculé) | Code hex pour l'affichage visuel |

`ColorHex` est une propriété calculée (pas sérialisée) : elle évite d'exposer des types
AvaloniaUI dans la couche Shared, tout en centralisant la correspondance couleur↔hex.

---

### 4.6 `CountdownPacket.cs` — US-06

Envoyé toutes les secondes pendant le compte à rebours.

| Champ | Type | Valeurs |
|-------|------|---------|
| `SecondsRemaining` | `int` | 5, 4, 3, 2, 1 → `"X..."`, 0 → `"GO !"` |

---

## 5. GameLogic — Interfaces applicatives

```
GameLogic/
└── Interfaces/
    ├── INavigationService.cs       ← NOUVEAU
    └── INetworkClientService.cs    ← NOUVEAU
```

> **Supprimé :** `GameLogic/Class1.cs` (placeholder vide)

### 5.1 `INavigationService.cs`

```csharp
public interface INavigationService
{
    void NavigateTo<TViewModel>() where TViewModel : class;
    void NavigateTo(object viewModel);
}
```

Cette interface réside dans **GameLogic** (couche Application) afin que les ViewModels puissent
naviguer sans connaître Avalonia ni la couche Infrastructure.
L'implémentation concrète (`NavigationService`) réside dans le Client.

---

### 5.2 `INetworkClientService.cs`

Interface complète du service réseau exposée à la couche Application :

```csharp
bool IsConnected { get; }
event Action<PacketType, BinaryReader>? PacketReceived;
event Action? Disconnected;
Task<bool> ConnectAsync(string address, int port, CancellationToken ct = default);
void SendReliable(IPacket packet);
void SendUnreliable(IPacket packet);
void PollEvents();
void Disconnect();
```

**Point clé :** l'événement `PacketReceived` passe un `BinaryReader` déjà positionné *après*
l'octet de type. L'abonné est responsable de la désérialisation, ce qui permet un seul point
de dispatch pour tous les types de paquets sans switch centralisé dans l'infrastructure.

---

## 6. Infrastructure — Implémentation réseau

```
Infrastructure/
└── Networking/
    └── LiteNetClientService.cs   ← NOUVEAU
```

> **Supprimé :** `Infrastructure/Class1.cs` (placeholder vide)

### `LiteNetClientService.cs`

Implémente `INetworkClientService` et `INetEventListener` (interface callback de LiteNetLib).

**Mécanisme de connexion async :**

LiteNetLib fonctionne par callbacks (pattern événementiel), pas par `async/await` nativement.
Pour intégrer proprement dans le monde async C#, on utilise un `TaskCompletionSource<bool>` :

```
ConnectAsync() appelé
    │
    ├─ NetManager.Connect() → lance la tentative UDP
    │
    ├─ Boucle de polling toutes les 15 ms (pour permettre à LiteNetLib de recevoir les ACKs)
    │   └─ Task.Delay(15, linkedCts.Token)
    │
    ├─ OnPeerConnected() appelé par LiteNetLib → TCS.TrySetResult(true)
    │   OU
    └─ OnPeerDisconnected() / timeout 10 s → TCS.TrySetResult(false)
```

**Gestion du timeout :** deux `CancellationTokenSource` sont combinés en un `LinkedTokenSource` :
- le `CancellationToken` passé par l'appelant (annulation externe)
- un token interne de 10 secondes (timeout réseau)

**Format des paquets sur le fil :**
```
NetDataWriter {
    byte  : PacketType
    byte[]: corps (issu de BinaryWriter → MemoryStream)
}
```

**Deux canaux de livraison :**

| Méthode | DeliveryMethod | Usage |
|---------|---------------|-------|
| `SendReliable()` | `ReliableOrdered` | Événements de jeu critiques |
| `SendUnreliable()` | `Unreliable` | Inputs temps-réel (BLOC 2+) |

La clé de connexion `"AsteroidOnline_v1"` empêche les connexions non autorisées d'autres
clients LiteNetLib au même serveur.

---

## 7. Client — Services

```
Client/
└── Services/
    └── NavigationService.cs   ← NOUVEAU
```

### `NavigationService.cs`

Implémentation de `INavigationService` spécifique à Avalonia.
Reçoit deux délégués lors de sa construction :

| Délégué | Signature | Rôle |
|---------|-----------|------|
| `viewModelFactory` | `Func<Type, object>` | Instancie un VM via la factory DI |
| `setCurrentViewModel` | `Action<object>` | Met à jour `MainWindowViewModel.CurrentViewModel` |

Ce design découple complètement le service de navigation d'Avalonia et de `MainWindowViewModel` :
les ViewModels utilisent uniquement `INavigationService` sans connaître ces détails.

---

## 8. Client — Convertisseurs XAML

```
Client/
└── Converters/
    ├── ConnectionStatusToBrushConverter.cs   ← NOUVEAU
    ├── PlayerColorToBrushConverter.cs         ← NOUVEAU
    ├── StringToBrushConverter.cs              ← NOUVEAU
    └── CountdownSegmentConverter.cs           ← NOUVEAU
```

Tous les convertisseurs suivent le même pattern : **singleton statique** accessible via
`ConverterNom.Instance`, compatible avec les bindings compilés d'Avalonia 11
(`AvaloniaUseCompiledBindingsByDefault=true`).

### 8.1 `ConnectionStatusToBrushConverter` — US-03

| ConnectionStatus | Couleur | Signification |
|-----------------|---------|---------------|
| `Idle` | `#88AACC` (bleu-gris) | État neutre |
| `Connecting` | `#FFAA00` (orange) | En cours |
| `Connected` | `#44FF88` (vert) | Succès |
| `Failed` | `#FF4444` (rouge) | Échec |

---

### 8.2 `PlayerColorToBrushConverter` — US-05

Convertit un `PlayerColor` (enum Domain) en `SolidColorBrush` Avalonia.
Table de correspondance pré-calculée (Dictionary) pour éviter des allocations répétées.

---

### 8.3 `StringToBrushConverter`

Convertit une chaîne hexadécimale (`"#FF4444"`) en `SolidColorBrush` via `Color.Parse()`.
Utilisé dans les DataTemplates où la source de binding est `LobbyPlayerInfo.ColorHex` (string),
qui vit dans Shared et ne peut donc pas retourner directement un `IBrush` Avalonia.

---

### 8.4 `CountdownSegmentConverter` — US-06

Prend `SecondsRemaining` (int) et `ConverterParameter` (index de segment 0–4).
Retourne `#00CFFF` si `segmentIndex < secondsRemaining`, sinon `#1E3A5F`.
Produit visuellement une barre de progression qui se vide au fur et à mesure.

---

## 9. Client — ViewModels

```
Client/
└── ViewModels/
    ├── ConnectViewModel.cs   ← NOUVEAU
    └── LobbyViewModel.cs     ← NOUVEAU
```

### 9.1 `ConnectViewModel.cs` — US-01, US-02, US-03, US-05

**Propriétés observables :**

| Propriété | Type | Rôle |
|-----------|------|------|
| `Pseudo` | `string` | Saisie du pseudo, déclenche CanExecute |
| `ServerAddress` | `string` | IP/hostname, défaut `127.0.0.1` |
| `ServerPort` | `int` | Port, défaut `7777` |
| `ConnectionStatus` | `ConnectionStatus` | Enum d'état (US-03) |
| `StatusMessage` | `string` | Texte du statut |
| `SelectedColorOption` | `PlayerColorOption` | Option de couleur sélectionnée |

**Type `PlayerColorOption` (record) :**
```csharp
public record PlayerColorOption(PlayerColor Color, string Name, string HexColor);
```
Regroupe les données nécessaires à l'affichage du sélecteur : l'enum métier, le nom localisé,
et la couleur hex pour le rendu visuel.

**Commande `ConnectCommand` (async RelayCommand) :**

```
1. ConnectionStatus = Connecting
2. S'abonner à PacketReceived
3. await networkService.ConnectAsync(...)
4a. Échec → ConnectionStatus = Failed, se désabonner
4b. Succès → SendReliable(ConnectRequestPacket { Pseudo, Color })
5. Attendre LobbyJoinedPacket dans OnPacketReceived
6. Dispatcher.UIThread.Post → ConnectionStatus = Connected
                            → NavigateTo<LobbyViewModel>()
```

**Condition CanExecute :**
- `Pseudo` non vide ET `ConnectionStatus != Connecting`
- Les deux propriétés sont annotées `[NotifyCanExecuteChangedFor(nameof(ConnectCommand))]`

---

### 9.2 `LobbyViewModel.cs` — US-04, US-06

**Propriétés observables :**

| Propriété | Type | Rôle |
|-----------|------|------|
| `Players` | `ObservableCollection<LobbyPlayerInfo>` | Liste liée au ItemsControl |
| `PlayerCount` | `int` | Affiché dans l'en-tête |
| `CountdownSeconds` | `int` | Secondes restantes (-1 = pas démarré) |
| `CountdownText` | `string` | "3...", "2...", "1...", "GO !" |
| `IsCountingDown` | `bool` | Contrôle la visibilité de la barre de segments |

**Gestion des paquets :**

```
PacketReceived
├── LobbyState  → HandleLobbyState() → Players.Clear() + Players.Add(...)
└── Countdown   → HandleCountdown()  → CountdownText, IsCountingDown
                                        si SecondsRemaining == 0 → se désabonner
```

Toutes les modifications de collections observables passent par `Dispatcher.UIThread.Post()`
pour éviter les exceptions cross-thread d'Avalonia.

---

## 10. Client — Vues AXAML

```
Client/
└── Views/
    ├── ConnectView.axaml + .axaml.cs   ← NOUVEAU
    └── LobbyView.axaml + .axaml.cs     ← NOUVEAU
```

### 10.1 `ConnectView.axaml` — US-01, US-03, US-05

**Structure :**
```
Grid (centré, 420px de large)
└── StackPanel
    ├── Titre "ASTEROID ONLINE" + sous-titre
    ├── TextBox  → Pseudo
    ├── Grid     → ServerAddress + ServerPort (colonnes *)
    ├── ListBox  → AvailableColors (sélecteur horizontal, DataTemplate avec cercle coloré)
    ├── Canvas   → Aperçu du vaisseau (Polygon triangle coloré) ← US-05
    ├── Button   → ConnectCommand
    └── TextBlock → StatusMessage (couleur via ConnectionStatusToBrushConverter)
```

**Sélecteur de couleur (US-05) :**
Le `ListBox` est lié à `AvailableColors` (liste statique dans le VM).
Chaque item affiche un `Ellipse` coloré + le nom de la couleur.
La sélection met à jour `SelectedColorOption` qui impacte le `Polygon` de prévisualisation.

**Indicateur de statut (US-03) :**
```xml
<TextBlock Foreground="{Binding ConnectionStatus,
    Converter={x:Static conv:ConnectionStatusToBrushConverter.Instance}}" />
```
Masqué via `IsVisible` si `StatusMessage` est vide.

---

### 10.2 `LobbyView.axaml` — US-04, US-06

**Structure :**
```
Grid (3 lignes : en-tête / liste / compte à rebours)
├── [Row 0] En-tête : "LOBBY" + nombre de joueurs
├── [Row 1] Border + ScrollViewer + ItemsControl
│   └── DataTemplate (LobbyPlayerInfo)
│       ├── Canvas avec Polygon vaisseau coloré ← US-05
│       └── TextBlock pseudo
└── [Row 2] StackPanel (compte à rebours ← US-06)
    ├── TextBlock CountdownText (avec transition Opacity 0.3s)
    └── StackPanel horizontal (5 Border segments, CountdownSegmentConverter)
```

**Correction appliquée :**
`Rectangle` n'a pas de propriété `CornerRadius` dans AvaloniaUI → remplacé par `Border`
avec `Background` (au lieu de `Fill`). Erreur AVLN2000 corrigée avant la livraison.

---

## 11. Fichiers existants modifiés

### 11.1 `MainWindowViewModel.cs`

**Avant :** propriété `Greeting` (string) uniquement.

**Après :**
```csharp
[ObservableProperty]
private object? _currentViewModel;

public void SetCurrentViewModel(object viewModel) { ... }
```

`CurrentViewModel` est le pivot de la navigation : le `ContentControl` de `MainWindow`
est lié à cette propriété, et le `ViewLocator` existant résout automatiquement la vue
correspondante par réflexion (`"ViewModel"` → `"View"` dans le nom de type).

---

### 11.2 `MainWindow.axaml`

**Avant :** `TextBlock` affichant `{Binding Greeting}`.

**Après :**
```xml
<ContentControl Content="{Binding CurrentViewModel}" />
```

Le `ViewLocator` (existant, non modifié) gère la résolution :
- `ConnectViewModel` → `ConnectView`
- `LobbyViewModel` → `LobbyView`
- etc.

Propriétés de fenêtre ajoutées : `Width=800`, `Height=600`, `MinWidth=640`, `MinHeight=480`,
`Background="#0A0A1A"` (thème spatial sombre).

---

### 11.3 `App.axaml.cs`

**Avant :** instanciait directement `new MainWindowViewModel()`.

**Après :** joue le rôle de **composition root** (DI manuel) :

```csharp
_networkService      = new LiteNetClientService();
_mainWindowViewModel = new MainWindowViewModel();
_navigationService   = new NavigationService(
    viewModelFactory: CreateViewModel,
    setCurrentViewModel: vm =>
        Dispatcher.UIThread.Post(() => _mainWindowViewModel.SetCurrentViewModel(vm)));

// Navigation initiale
_navigationService.NavigateTo<ConnectViewModel>();

// Nettoyage à la fermeture
desktop.Exit += (_, _) => _networkService.Dispose();
```

La méthode `CreateViewModel(Type)` est la factory DI :
```csharp
private object CreateViewModel(Type type) => type switch {
    _ when type == typeof(ConnectViewModel) => new ConnectViewModel(_networkService!, _navigationService!),
    _ when type == typeof(LobbyViewModel)   => new LobbyViewModel(_networkService!, _navigationService!),
    _ => throw new InvalidOperationException(...)
};
```

> Pour les blocs suivants, ajouter simplement un `if` dans `CreateViewModel`
> pour enregistrer les nouveaux ViewModels (`GameViewModel`, `ResultViewModel`, etc.).

---

## 12. Architecture & flux de données

### 12.1 Diagramme de navigation

```
App.axaml.cs (composition root)
    │
    ├─ crée: MainWindowViewModel
    ├─ crée: LiteNetClientService
    ├─ crée: NavigationService
    └─ NavigateTo<ConnectViewModel>()
                │
                ▼
        ConnectViewModel
        [Pseudo / Adresse / Couleur / ConnectCommand]
                │
                │ ConnectCommand.Execute()
                ▼
        LiteNetClientService.ConnectAsync()
                │
                │ OnPeerConnected → TCS.SetResult(true)
                ▼
        SendReliable(ConnectRequestPacket)
                │
                │ ← LobbyJoinedPacket reçu
                ▼
        NavigateTo<LobbyViewModel>()
                │
                ▼
        LobbyViewModel
        [Players / CountdownText / CountdownSeconds]
                │
                │ ← LobbyStatePacket reçu → mise à jour Players
                │ ← CountdownPacket reçu  → compte à rebours
                ▼
        (BLOC 2+) NavigateTo<GameViewModel>() quand SecondsRemaining == 0
```

---

### 12.2 Flux réseau d'un paquet

```
[Serveur envoie LobbyStatePacket]
        │
        ▼ UDP (ReliableOrdered)
LiteNetClientService.OnNetworkReceive()
    │ extrait byte PacketType
    │ extrait byte[] body
    │ crée MemoryStream + BinaryReader
    │
    ▼
PacketReceived?.Invoke(PacketType.LobbyState, binaryReader)
        │
        ▼ (abonné dans LobbyViewModel)
LobbyViewModel.OnPacketReceived()
    │ case LobbyState → HandleLobbyState(reader)
    │     packet.Deserialize(reader)
    │     Dispatcher.UIThread.Post(() => Players.Clear() + Add(...))
    │
    ▼
[Binding Avalonia] ItemsControl.ItemsSource = Players
        │
        ▼
[Rendu] Liste des joueurs mise à jour dans LobbyView
```

---

### 12.3 Graphe de dépendances (après BLOC 1)

```
Domain (aucune dépendance)
    ▲
Shared (→ Domain)
    ▲           ▲
GameLogic    (→ Domain, Shared)
    ▲
Infrastructure (→ GameLogic, Shared, LiteNetLib)
    ▲
Client (→ Infrastructure, Shared, Avalonia, CommunityToolkit.Mvvm)

Server (→ Infrastructure)   [non modifié dans ce bloc]
```

---

## 13. Décisions techniques & compromis

### 13.1 DI manuel plutôt que Microsoft.Extensions.DependencyInjection

**Décision :** pas de framework DI tiers dans ce bloc.

**Raison :** la factory `CreateViewModel(Type)` dans `App.axaml.cs` est suffisante pour
3 ViewModels. Ajouter `Microsoft.Extensions.DependencyInjection` introduirait une dépendance
supplémentaire et de la complexité de configuration sans bénéfice tangible à ce stade.

**Impact futur :** si le nombre de ViewModels dépasse ~8 et que les graphes de dépendances
deviennent complexes, migrer vers un vrai conteneur DI est simple (remplacer `CreateViewModel`
par une résolution via `IServiceProvider`).

---

### 13.2 Sérialisation binaire (BinaryWriter/Reader) plutôt que JSON

**Décision :** protocole binaire maison avec `BinaryWriter`/`BinaryReader`.

**Raison :** minimisation de la bande passante (critique pour UDP temps-réel), simplicité
d'implémentation, et compatibilité directe avec `NetDataWriter`/`NetDataReader` de LiteNetLib.

**Compromis :** moins lisible pour le debug qu'un format textuel. Compensé par des commentaires
XML sur chaque paquet décrivant précisément les champs.

---

### 13.3 `LobbyPlayerInfo.ColorHex` dans Shared

**Décision :** propriété calculée `ColorHex` ajoutée à `LobbyPlayerInfo` (couche Shared).

**Raison :** les DataTemplates AXAML doivent binder sur une `string` pour passer dans
`StringToBrushConverter`. Plutôt que de créer un ViewModel wrapper dans le Client
(`LobbyPlayerInfoViewModel`), centraliser la correspondance couleur↔hex dans Shared est
plus simple et suffit pour ce stade.

**Compromis :** `LobbyPlayerInfo` a une légère responsabilité de présentation (couleur hex)
qui pourrait appartenir à la couche Client. Acceptable car la correspondance est stable et
ne dépend d'aucune bibliothèque externe.

---

### 13.4 `TaskCompletionSource` pour ConnectAsync

**Décision :** utiliser un `TaskCompletionSource<bool>` pour rendre `ConnectAsync` awaitable.

**Raison :** LiteNetLib est fondamentalement événementiel. Toute la logique de connexion
passe par les callbacks `INetEventListener`. La TCS est le pont standard entre le monde
événementiel et le monde async/await.

**Point de vigilance :** la TCS est réinitialisée à chaque appel de `ConnectAsync`. Si une
reconnexion est tentée (US-28, BLOC 6), s'assurer que l'ancienne TCS est proprement abandonnée.

---

### 13.5 Suppression de LiteNetLib du Client.csproj

**Décision :** LiteNetLib retiré du projet Client (il n'était utilisé que transitivement).

**Raison :** le Client ne doit pas manipuler LiteNetLib directement. Toute communication
passe par `INetworkClientService`. Retirer la référence explicite renforce ce couplage faible.

---

## 14. Points d'attention pour la suite

### Pour BLOC 2 (Pilotage & Physique)

- **`GameViewModel`** doit être ajouté dans `App.axaml.cs → CreateViewModel()`
- **`LobbyViewModel.HandleCountdown()`** contient un TODO pour la navigation vers GameViewModel :
  ```csharp
  // La navigation vers GameViewModel sera implémentée dans BLOC 7.
  ```
- **Polling réseau :** dans le BLOC 2, le polling de `LiteNetClientService.PollEvents()`
  devra être appelé à chaque tick de la boucle de jeu (60 Hz). Actuellement, le polling
  se fait uniquement pendant `ConnectAsync`. Un timer ou la boucle de jeu devra prendre
  le relais une fois la connexion établie.
- **`INetworkClientService.SendUnreliable()`** est défini mais pas encore utilisé.
  Il sera sollicité par `InputHandler` (BLOC 2, US-07) pour envoyer les inputs en UDP.

### Aspects non couverts dans ce bloc (intentionnellement)

| Sujet | US | Bloc prévu |
|-------|----|------------|
| Boucle de jeu serveur | US-27 | BLOC 6 |
| Session serveur / gestion joueurs côté serveur | US-02 | BLOC 5 |
| Physique des entités | US-08 | BLOC 2 |
| Reconnexion automatique | US-28 | BLOC 6 |
| HUD + overlay | US-29 | BLOC 7 |

---

*Document généré le 2026-04-03 — AsteroidOnline BLOC 1*
