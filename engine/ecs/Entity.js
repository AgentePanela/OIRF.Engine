export class Entity {
    constructor(id) {
        this.id = id;
        this.name = null; 
        this.components = new Map();
        this.active = true;
    }

    addComponent(component) {
        const name = component.constructor.name;
        this.components.set(name, component);
        return this;
    }

    removeComponent(componentType) {
        const name = typeof componentType === 'string' ? componentType : componentType.name;
        this.components.delete(name);
        return this;
    }

    getComponent(componentType) {
        const name = typeof componentType === 'string' ? componentType : componentType.name;
        return this.components.get(name);
    }

    hasComponent(componentType) {
        const name = typeof componentType === 'string' ? componentType : componentType.name;
        return this.components.has(name);
    }

    getComponents() {
        return Array.from(this.components.values());
    }

    destroy() {
        this.active = false;
        this.components.clear();
    }
}