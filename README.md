## Repository Structure

### Source Code (`src/`)
differennt projects of the solution are in src folder
- `NodeVision.Core` - Core data types and scene model
- `NodeVision.Visualisation` - Presentation state management
- `NodeVision.Rendering` - Rendering abstraction layer
- `NodeVision.App` - Presentation App
- `NodeVision.Designer` - App for designer tool
- `NodeVision.Inference` - Library for ML inference


## Branches
- `main` - stable code
- `feature/__` - Implementing new features (e.g., `feature/gesture-recognition`)
- `experiment/__` - Trying out ideas or prototypes (e.g., `experiment/ml-pose-tracking`)

**Workflow:**
1. Create a branch from `main` for new work
2. Use prefixes (`feature/`, `experiment/`, etc.)
3. Submit pull requests to `main` for review


## Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
