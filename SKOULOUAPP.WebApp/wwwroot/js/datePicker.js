window.SKOULOUAPPDatePicker = {
    instances: {},

    init: function (element, dotNetRef, initialValue) {
        if (!element || typeof flatpickr === 'undefined') {
            return;
        }

        // Bestehende Instanz aufräumen, falls vorhanden.
        if (element._flatpickr) {
            element._flatpickr.destroy();
        }

        const picker = flatpickr(element, {
            locale: 'fr',
            dateFormat: 'd.m.Y',
            altInput: false,
            allowInput: true,
            maxDate: 'today',
            defaultDate: initialValue || null,
            disableMobile: true,
            onChange: function (selectedDates, dateStr, instance) {
                let value = '';
                if (selectedDates.length > 0) {
                    const d = selectedDates[0];
                    const yyyy = d.getFullYear();
                    const mm = String(d.getMonth() + 1).padStart(2, '0');
                    const dd = String(d.getDate()).padStart(2, '0');
                    value = `${yyyy}-${mm}-${dd}`;
                }
                dotNetRef.invokeMethodAsync('OnDateChanged', value);
            }
        });

        return true;
    }
};
