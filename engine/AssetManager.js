export class AssetManager {
    constructor() {
        this.sprites = new Map();
        this.scripts = new Map();
        this.nextId = 1;
    }

    createSprite(name, config = {}) {
        const sprite = {
            id: this.nextId++,
            name: name || `Sprite_${this.nextId}`,
            color: config.color || '#4ade80',
            width: config.width || 32,
            height: config.height || 32,
            shape: config.shape || 'rectangle',
            texture: config.texture || null
        };
        
        this.sprites.set(sprite.id, sprite);
        return sprite;
    }

    getSprite(id) {
        return this.sprites.get(id);
    }

    getAllSprites() {
        return Array.from(this.sprites.values());
    }

    getScript(id) {
        return this.scripts.get(id);
    }

    getAllScripts() {
        return Array.from(this.scripts.values());
    }
}