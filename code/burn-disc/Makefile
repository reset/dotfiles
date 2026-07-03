# burn-disc — build entry points. `make help` lists targets.

PROJECT   := src/BurnDisc/BurnDisc.csproj
DIST      := dist
BINARY    := $(DIST)/burn-disc
SHIM      := $(HOME)/bin/burn-disc

.DEFAULT_GOAL := help

.PHONY: help
help: ## List targets
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  %-12s %s\n", $$1, $$2}'

.PHONY: build
build: ## Build (Debug)
	dotnet build

.PHONY: test
test: ## Run the test suite
	dotnet test

.PHONY: run
run: ## Run against ARGS, e.g. make run ARGS='game.7z --dry-run'
	dotnet run --project $(PROJECT) -- $(ARGS)

.PHONY: publish
publish: ## Compile a release binary to dist/burn-disc
	dotnet publish $(PROJECT) -c Release -o $(DIST)
	@echo "Built $(BINARY)"

.PHONY: install
install: publish ## Publish and point ~/bin/burn-disc at the release binary
	@printf '#!/usr/bin/env bash\nset -euo pipefail\nbin="$(HOME)/code/burn-disc/$(BINARY)"\n[[ -x "$$bin" ]] || { echo >&2 "burn-disc not built — run: make -C ~/code/burn-disc publish"; exit 1; }\nexec "$$bin" "$$@"\n' > $(SHIM)
	@chmod +x $(SHIM)
	@echo "Installed shim -> $(SHIM)"

.PHONY: clean
clean: ## Remove build output
	dotnet clean
	rm -rf $(DIST)
