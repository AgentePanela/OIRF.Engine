import { Entity } from './Entity.js';

export class World {
    constructor() {
        this.entities = new Map();
        this.systems = new Map();
        this.nextEntityId = 1;
        this.entityTypes = new Map();
    }

    createEntity(type = 'generic') {
        const id = this.nextEntityId++;
        const entity = new Entity(id);
        this.entities.set(id, entity);
        this.entityTypes.set(id, type);
        return entity;
    }

    removeEntity(entityId) {
        const entity = this.entities.get(entityId);
        if (entity && entity.hasComponent('ScriptComponent')) {
            const scriptComponent = entity.getComponent('ScriptComponent');
            const scriptSystem = this.getSystem('ScriptSystem');
            if (scriptSystem && scriptComponent.scriptId) {
                scriptSystem.cleanupScript(entityId, scriptComponent.scriptId);
            }
        }
        
        this.entities.delete(entityId);
        this.entityTypes.delete(entityId);
    }

    getEntity(entityId) {
        return this.entities.get(entityId);
    }

    getEntities() {
        return Array.from(this.entities.values());
    }

    getEntitiesByType(type) {
        return this.getEntities().filter(entity => 
            this.entityTypes.get(entity.id) === type
        );
    }

    addSystem(system) {
        const name = system.constructor.name;
        system.world = this;
        this.systems.set(name, system);
        if (system.init) {
            system.init();
        }
    }

    getSystem(name) {
        return this.systems.get(name);
    }

    getSystems() {
        return Array.from(this.systems.values());
    }

    update(deltaTime) {
        for (const system of this.systems.values()) {
            if (system.update) {
                system.update(deltaTime);
            }
        }
    }

    getEntityCount() {
        return this.entities.size;
    }

    getSystemCount() {
        return this.systems.size;
    }

    query(componentTypes) {
        return this.getEntities().filter(entity => {
            return componentTypes.every(type => entity.hasComponent(type));
        });
    }
}