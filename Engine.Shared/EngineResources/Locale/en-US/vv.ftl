# View Variables window
engine-vv-window-title = View Variables
engine-vv-window-title-target = View Variables - {$target}
engine-vv-tab-client = Client
engine-vv-tab-server = Server
engine-vv-refresh = Refresh
engine-vv-up = Up
engine-vv-components = Components:
engine-vv-add-component = Add Component
engine-vv-search-component = Search component...
engine-vv-key-placeholder = key
engine-vv-member-read-only = {$name} (read)
engine-vv-nullable-set = set
engine-vv-waiting-server = Waiting on the server...

# Fallbacks for when a failed action gives no reason
engine-vv-fail-add-element = couldn't add element
engine-vv-fail-remove-element = couldn't remove element
engine-vv-fail-add-component = couldn't add component
engine-vv-fail-remove-component = couldn't remove component

# Resolving a path
engine-vv-error-entity-gone = Entity #{$uid} no longer exists.
engine-vv-error-component-missing = Component '{$name}' not found on entity #{$uid}.
engine-vv-error-unpinned = Detached object is no longer pinned.
engine-vv-error-unknown-root = Unknown VV root kind.
engine-vv-error-null = '{$step}' is null.
engine-vv-error-no-member = No member '{$name}' on {$type}.
engine-vv-error-index-range = Index {$index} is out of range.
engine-vv-error-not-indexable = {$type} is not indexable.
engine-vv-error-not-dictionary = {$type} is not a dictionary.
engine-vv-error-key-missing = Key '{$key}' not found.
engine-vv-error-step-unsupported = Step '{$step}' is not supported.

# Writing
engine-vv-error-write-root = Cannot write to a root object directly.
engine-vv-error-read-only = '{$name}' is read-only.
engine-vv-error-read-only-chain = '{$step}' is read-only, so the change wouldn't be written back.
engine-vv-error-elements-read-only = {$type} elements can't be written.
engine-vv-error-entries-read-only = {$type} entries can't be written.
engine-vv-error-element-type = Can't tell the element type of {$type}.
engine-vv-error-value-type = Can't tell the value type of {$type}.
engine-vv-error-invalid-uid = '{$text}' is not a valid entity uid.
engine-vv-error-not-net-entity = '{$text}' is not a net entity id.
engine-vv-error-text = <error: {$message}>

# Adding and removing elements
engine-vv-error-key-required = A key is required.
engine-vv-error-key-exists = That key already exists.
engine-vv-error-array-fixed = Arrays can't be resized.
engine-vv-error-list-fixed = This list can't be resized.
engine-vv-error-dictionary-fixed = This dictionary can't be resized.
engine-vv-error-not-resizable = {$type} isn't a resizable collection.
engine-vv-error-no-parameterless-ctor = {$type} doesn't have a parameterless constructor - can't create one automatically.
engine-vv-error-remove-root = Cannot remove a root object.
engine-vv-error-elements-fixed = {$type} elements can't be removed.
engine-vv-error-entries-fixed = {$type} entries can't be removed.
engine-vv-error-remove-collection-only = Only collection/dictionary elements can be removed.

# Components
engine-vv-error-unknown-component = Unknown component '{$name}'.
engine-vv-error-unknown-component-type = Unknown component type '{$name}'.
engine-vv-error-already-has-component = Entity already has '{$name}'.
engine-vv-error-not-a-component = Not a component.

# Talking to the other side
engine-vv-error-not-connected = Not connected.
engine-vv-error-malformed-path = Malformed VV path.
engine-vv-error-not-server-path = That path is not addressed at the server.
engine-vv-error-detached-local-only = Detached roots are local-only.
engine-vv-error-net-entity-missing = {$netEntity} does not exist here.
engine-vv-error-unknown-write-op = Unknown VV write op {$op}.
