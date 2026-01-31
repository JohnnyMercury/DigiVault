// DigiVault JavaScript

document.addEventListener('DOMContentLoaded', function() {
    // Update cart count on page load
    updateCartCount();

    // Auto-hide alerts after 5 seconds
    const alerts = document.querySelectorAll('.alert-dismissible');
    alerts.forEach(alert => {
        setTimeout(() => {
            const closeBtn = alert.querySelector('.btn-close');
            if (closeBtn) closeBtn.click();
        }, 5000);
    });
});

// Update cart count in navbar
function updateCartCount() {
    fetch('/Cart/GetCount')
        .then(response => response.json())
        .then(data => {
            const badge = document.getElementById('cart-count');
            if (badge) {
                if (data.count > 0) {
                    badge.textContent = data.count;
                    badge.style.display = 'inline';
                } else {
                    badge.style.display = 'none';
                }
            }
        })
        .catch(() => {});
}

// Add to cart with AJAX
function addToCart(productId, quantity = 1) {
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    fetch('/Cart/Add', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
            'X-Requested-With': 'XMLHttpRequest'
        },
        body: `ProductId=${productId}&Quantity=${quantity}&__RequestVerificationToken=${encodeURIComponent(token)}`
    })
    .then(response => response.json())
    .then(data => {
        if (data.success) {
            const badge = document.getElementById('cart-count');
            if (badge) {
                badge.textContent = data.cartCount;
                badge.style.display = 'inline';
            }
            showToast('Item added to cart!', 'success');
        } else {
            showToast(data.message || 'Failed to add item', 'danger');
        }
    })
    .catch(() => {
        showToast('An error occurred', 'danger');
    });
}

// Simple toast notification
function showToast(message, type = 'info') {
    const toast = document.createElement('div');
    toast.className = `alert alert-${type} position-fixed bottom-0 end-0 m-3`;
    toast.style.zIndex = '9999';
    toast.style.minWidth = '250px';
    toast.innerHTML = `
        <div class="d-flex align-items-center">
            <i class="bi bi-${type === 'success' ? 'check-circle' : 'info-circle'} me-2"></i>
            ${message}
        </div>
    `;

    document.body.appendChild(toast);

    setTimeout(() => {
        toast.style.transition = 'opacity 0.3s';
        toast.style.opacity = '0';
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

// Format currency
function formatCurrency(amount) {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: 'USD'
    }).format(amount);
}
