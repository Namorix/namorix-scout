IMAGE := izerocs/namorix-scout
VERSION := 0.2.0
PLATFORMS := linux/amd64,linux/arm64

.PHONY: build push

build:
	docker buildx build --platform $(PLATFORMS) \
		-t $(IMAGE):$(VERSION) \
		-t $(IMAGE):latest \
		--build-context namorix=../namorix .

push:
	docker buildx build --platform $(PLATFORMS) \
		-t $(IMAGE):$(VERSION) \
		-t $(IMAGE):latest \
		--push \
		--build-context namorix=../namorix .
